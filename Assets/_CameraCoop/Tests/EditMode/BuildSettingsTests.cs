using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CameraCoop.Tests
{
    public class BuildSettingsTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void MacPostprocess_EnablesAutomaticGraphicsSwitchingInInfoPlist(bool startsWithoutKey)
        {
            string appPath = Path.Combine(Path.GetTempPath(), "CameraCoopBuildSettings-" + Guid.NewGuid(), "CameraCoop.app");
            string plistPath = Path.Combine(appPath, "Contents", "Info.plist");
            Directory.CreateDirectory(Path.GetDirectoryName(plistPath));
            File.WriteAllText(plistPath,
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><plist version=\"1.0\"><dict>"
                + "<key>CFBundleName</key><string>CameraCoop</string>"
                + (startsWithoutKey ? "" : "<key>NSSupportsAutomaticGraphicsSwitching</key><false/>")
                + "</dict></plist>");

            try
            {
                Type payloadType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType("CameraCoop.EditorTools.CameraCoopBuildPayload"))
                    .FirstOrDefault(type => type != null);
                Assert.IsNotNull(payloadType, "CameraCoopBuildPayload Editor type이 필요하다");
                MethodInfo method = payloadType.GetMethod("ApplyMacPlayerSettings",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.IsNotNull(method, "macOS postbuild 설정 함수가 필요하다");

                method.Invoke(null, new object[] { BuildTarget.StandaloneOSX, appPath });
                method.Invoke(null, new object[] { BuildTarget.StandaloneOSX, appPath });

                XDocument plist = XDocument.Load(plistPath);
                XElement dict = plist.Root.Element("dict");
                XElement[] keys = dict.Elements("key")
                    .Where(key => key.Value == "NSSupportsAutomaticGraphicsSwitching").ToArray();
                Assert.AreEqual(1, keys.Length, "graphics switching key는 중복되면 안 된다");
                Assert.AreEqual("true", keys[0].ElementsAfterSelf().First().Name.LocalName,
                    "graphics switching key는 true여야 한다");
                XElement bundleName = dict.Elements("key").Single(key => key.Value == "CFBundleName");
                Assert.AreEqual("CameraCoop", bundleName.ElementsAfterSelf().First().Value,
                    "기존 plist 값은 보존해야 한다");
            }
            finally
            {
                Directory.Delete(Path.GetDirectoryName(appPath), true);
            }
        }

        [Test]
        public void ThermalProjectSettings_EnableVSyncAndDisableMacRetinaRendering()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] vSyncSettings = File.ReadAllLines(Path.Combine(root, "ProjectSettings", "QualitySettings.asset"))
                .Select(line => line.Trim()).Where(line => line.StartsWith("vSyncCount:")).ToArray();
            CollectionAssert.AreEqual(new[] { "vSyncCount: 1", "vSyncCount: 1" }, vSyncSettings,
                "모든 quality level은 display refresh에 맞춰 vSync해야 한다");

            string playerSettings = File.ReadAllText(Path.Combine(root, "ProjectSettings", "ProjectSettings.asset"));
            StringAssert.Contains("macRetinaSupport: 0", playerSettings,
                "Intel Mac render pixel 수를 줄이도록 Retina support를 꺼야 한다");
        }

        [Test]
        public void Postprocess_DoesNotReadMacPlistForWindowsBuild()
        {
            Type payloadType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("CameraCoop.EditorTools.CameraCoopBuildPayload"))
                .FirstOrDefault(type => type != null);
            MethodInfo method = payloadType.GetMethod("ApplyMacPlayerSettings",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.DoesNotThrow(() => method.Invoke(null,
                new object[] { BuildTarget.StandaloneWindows64, "missing-build-output" }));
        }
    }
}
