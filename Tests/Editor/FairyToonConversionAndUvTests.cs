using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NW.FairyToon.Editor.Tests
{
    public class FairyToonConversionAndUvTests
    {
        [TestCase(-1, true, 2460)]
        [TestCase(3000, true, 2450)]
        [TestCase(3100, true, 2450)]
        [TestCase(2463, true, 2463)]
        [TestCase(3000, false, 3000)]
        [TestCase(3100, false, 3100)]
        public void TransparentHairConversionUsesTheSelectedCoverageQueue(int sourceQueue, bool cutout, int expectedQueue)
        {
            Shader shader = Shader.Find("Hidden/lilToonTransparent");
            if (shader == null) Assert.Ignore("The converter integration test requires lilToon.");
            var source = new Material(shader) { name = "Test Hair", renderQueue = sourceQueue };
            var target = new Material(Shader.Find(FairyToon.ShaderName));
            try
            {
                var options = new FairyToonConvertOptions { TransparentHairToCutout = cutout, UseProjectProfiles = false };
                var report = FairyToonConverter.Convert(source, target, options);
                Assert.IsTrue(report.Success, string.Join(", ", report.Errors));
                Assert.AreEqual(cutout ? FairyToonMaterialSetup.Cutout : FairyToonMaterialSetup.Transparent, report.Mode);
                Assert.AreEqual(expectedQueue, target.renderQueue);
                Assert.AreEqual(cutout, target.IsKeywordEnabled("_ALPHATEST_ON"));
                Assert.AreEqual(cutout, target.GetShaderPassEnabled(FairyToon.CharacterMaskLightMode));
                Assert.AreEqual(sourceQueue, source.rawRenderQueue);
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void FurModeTransitionsSetShellDefaultsAndKeepLaterEdits()
        {
            var material = new Material(Shader.Find(FairyToon.ShaderName));
            try
            {
                Assert.IsTrue(FairyToonMaterialSetup.SetRenderingMode(material, FairyToonMaterialSetup.Fur));
                foreach (int mode in new[] { FairyToonMaterialSetup.FurTwoPass, FairyToonMaterialSetup.FurCutout, FairyToonMaterialSetup.FurTransparent })
                {
                    Assert.IsTrue(FairyToonMaterialSetup.SetFurMode(material, mode));
                    int expected = mode == FairyToonMaterialSetup.FurTransparent ? 0 : 1;
                    Assert.AreEqual(expected, material.GetFloat("_FurZWrite"), "Fur depth, mode " + mode);
                    Assert.AreEqual(expected, material.GetFloat("_FurAlphaToMask"), "Fur alpha to coverage, mode " + mode);
                    Assert.AreEqual(mode == FairyToonMaterialSetup.FurCutout ? 1 : 0, material.GetFloat("_AlphaToMask"));
                    material.SetFloat("_FurZWrite", 1 - expected);
                    material.SetFloat("_FurAlphaToMask", 1 - expected);
                    FairyToonMaterialSetup.Apply(material);
                    Assert.AreEqual(1 - expected, material.GetFloat("_FurZWrite"));
                    Assert.AreEqual(1 - expected, material.GetFloat("_FurAlphaToMask"));
                }
            }
            finally { Object.DestroyImmediate(material); }
        }

        [TestCase("_Main2ndTex", true)]
        [TestCase("_Main3rdTex", false)]
        public void ResetUvRespectsEachPreviewCallbackAndUndoesAssetChanges(string tex, bool keepPreview)
        {
            var first = new Material(Shader.Find(FairyToon.ShaderName));
            var second = new Material(first);
            var targets = new Object[] { first, second };
            var scroll = tex + "_ScrollRotate";
            var initialScroll = new Vector4(1f, 2f, 3f, 4f);
            var offset = new Vector2(0.25f, 0.5f);
            try
            {
                second.SetTextureOffset(tex, offset);
                foreach (Material material in new[] { first, second })
                {
                    material.SetVector(scroll, initialScroll);
                    material.SetFloat(tex + "Angle", 1f);
                    material.SetFloat(tex + "IsDecal", 1f);
                }
                MaterialProperty[] properties = MaterialEditor.GetMaterialProperties(targets);
                var offered = new HashSet<string>();
                foreach (MaterialProperty property in properties)
                {
                    property.applyPropertyCallback = (changed, mask, previous) =>
                    {
                        offered.Add(changed.name);
                        if (changed.name == tex)
                        {
                            Assert.AreEqual(30, mask, "Offer all four scale/offset axes to the preview.");
                            Assert.AreEqual(new Vector4(1, 1, 0, 0), changed.textureScaleAndOffset);
                        }
                        return keepPreview && changed.name != tex + "IsDecal";
                    };
                }
                Undo.IncrementCurrentGroup();
                FairyToonGUI.ResetLayerUv(properties, scroll);
                Assert.IsTrue(offered.IsSupersetOf(new[] { tex, scroll, tex + "Angle", tex + "IsDecal" }));
                foreach (Material material in new[] { first, second })
                {
                    Assert.AreEqual(keepPreview ? initialScroll : Vector4.zero, material.GetVector(scroll));
                    Assert.AreEqual(keepPreview ? 1f : 0f, material.GetFloat(tex + "Angle"));
                    Assert.AreEqual(0f, material.GetFloat(tex + "IsDecal"), "A declined callback must still reset the asset.");
                }
                Assert.AreEqual(keepPreview ? offset : Vector2.zero, second.GetTextureOffset(tex));
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.AreEqual(initialScroll, second.GetVector(scroll));
                Assert.AreEqual(1f, first.GetFloat(tex + "IsDecal"));
                Assert.AreEqual(1f, second.GetFloat(tex + "Angle"));
                Assert.AreEqual(offset, second.GetTextureOffset(tex));
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }
    }
}
