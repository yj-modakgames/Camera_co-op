using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CameraCoop.Tests
{
    public class TrackerLauncherTests
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private GameObject rig;
        private TrackerLauncher launcher;
        private Text label;

        [TearDown]
        public void TearDown()
        {
            if (launcher != null)
            {
                SetProbe("failStop", false);
                SetProbe("throwOnStop", false);
            }
            if (rig != null)
            {
                Object.DestroyImmediate(rig);
            }
        }

        [Test]
        public void PublicApi_ExposesProcessStatusAndExplicitCommands()
        {
            AssertPublicMethod("StartTracker", typeof(bool));
            AssertPublicMethod("StopTracker", typeof(void));
            AssertPublicMethod("RefreshStatus", typeof(void));
            AssertPublicProperty("IsRunning", typeof(bool));
            AssertPublicProperty("LastError", typeof(string));
        }

        [Test]
        public void LegacyToggle_PreservesLaunchErrorInStatusAndLabel()
        {
            CreateLauncher();
            SetProbe("failStart", true);
            SetProbe("startError", "캠: setup 먼저 실행");

            launcher.OnClickToggle();

            Assert.IsFalse(launcher.IsRunning);
            Assert.AreEqual("캠: setup 먼저 실행", LastError);
            Assert.AreEqual(LastError, label.text);
            Assert.AreEqual(1, GetProbe<int>("startCalls"));
        }

        [TestCase("Start")]
        [TestCase("RefreshStatus")]
        [TestCase("Update")]
        public void PassiveCallback_DoesNotEraseLaunchFailure(string callback)
        {
            CreateLauncher();
            SetProbe("failStart", true);
            Assert.IsFalse(StartTracker());
            string error = LastError;

            Invoke(callback);

            Assert.AreEqual(error, LastError);
            Assert.IsNotEmpty(LastError);
            Assert.AreEqual(error, label.text);
            Assert.AreEqual(1, GetProbe<int>("startCalls"));
        }

        [Test]
        public void RepeatedStart_DoesNotLaunchAnotherProcess()
        {
            CreateLauncher();
            Assert.IsTrue(StartTracker());

            bool started = StartTracker();

            Assert.IsTrue(started);
            Assert.IsTrue(launcher.IsRunning);
            Assert.AreEqual(1, GetProbe<int>("startCalls"));
            Assert.AreEqual("캠 끄기", label.text);
        }

        [TestCase(null)]
        [TestCase("")]
        public void FailedStartWithoutDetail_StillExposesAnError(string error)
        {
            CreateLauncher();
            SetProbe("failStart", true);
            SetProbe("startError", error);

            bool started = StartTracker();

            Assert.IsFalse(started);
            Assert.IsFalse(launcher.IsRunning);
            Assert.IsNotEmpty(LastError);
            Assert.AreEqual(LastError, label.text);
        }

        [Test]
        public void UnexpectedExit_UpdateKeepsFailureAndCleansUpOnce()
        {
            CreateLauncher();
            Assert.IsTrue(StartTracker());
            SetProbe("running", false);

            Invoke("Update");
            string error = LastError;
            Invoke("RefreshStatus");

            Assert.IsFalse(launcher.IsRunning);
            Assert.IsNotEmpty(error);
            Assert.AreEqual(error, LastError);
            Assert.AreEqual(error, label.text);
            Assert.AreEqual(1, GetProbe<int>("startCalls"));
            Assert.AreEqual(1, GetProbe<int>("stopCalls"));
        }

        [Test]
        public void RepeatedStop_CleansUpOnlyOwnedProcessOnce()
        {
            CreateLauncher();
            Assert.IsTrue(StartTracker());

            Invoke("StopTracker");
            Invoke("StopTracker");

            Assert.IsFalse(launcher.IsRunning);
            Assert.AreEqual(1, GetProbe<int>("stopCalls"));
            Assert.IsEmpty(LastError);
            Assert.AreEqual("캠 켜기", label.text);
        }

        [Test]
        public void StopWithoutStart_DoesNotRequestProcessCleanup()
        {
            CreateLauncher();

            Invoke("StopTracker");
            Invoke("OnApplicationQuit");
            Invoke("OnDestroy");

            Assert.AreEqual(0, GetProbe<int>("stopCalls"));
            Assert.IsFalse(launcher.IsRunning);
        }

        [TestCase("OnApplicationQuit")]
        [TestCase("OnDestroy")]
        public void LifecycleCleanup_StopsOwnedProcessOnce(string callback)
        {
            CreateLauncher();
            Assert.IsTrue(StartTracker());

            Invoke(callback);
            Invoke("StopTracker");

            Assert.IsFalse(launcher.IsRunning);
            Assert.AreEqual(1, GetProbe<int>("stopCalls"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FailedStop_KeepsRunningProcessAvailableForRetry(bool throwOnStop)
        {
            CreateLauncher();
            Assert.IsTrue(StartTracker());
            SetProbe("failStop", true);
            SetProbe("throwOnStop", throwOnStop);

            Invoke("StopTracker");

            Assert.IsTrue(launcher.IsRunning);
            Assert.IsNotEmpty(LastError);
            Assert.AreEqual(LastError, label.text);
            SetProbe("failStop", false);
            SetProbe("throwOnStop", false);
            Invoke("StopTracker");
            Assert.IsFalse(launcher.IsRunning);
            Assert.AreEqual(2, GetProbe<int>("stopCalls"));
            Assert.IsEmpty(LastError);
        }

        [Test]
        public void UnexpectedExit_DoesNotReplaceAnEarlierStopFailure()
        {
            CreateLauncher();
            Assert.IsTrue(StartTracker());
            SetProbe("failStop", true);
            Invoke("StopTracker");
            string error = LastError;
            SetProbe("running", false);

            Invoke("RefreshStatus");

            Assert.AreEqual(error, LastError);
            Assert.AreEqual(error, label.text);
        }

        [Test]
        public void ExplicitStart_ClearsPreviousFailure()
        {
            CreateLauncher();
            SetProbe("failStart", true);
            Assert.IsFalse(StartTracker());
            SetProbe("failStart", false);

            Assert.IsTrue(StartTracker());

            Assert.IsEmpty(LastError);
            Assert.AreEqual("캠 끄기", label.text);
        }

        [Test]
        public void ExplicitStop_ClearsPreviousFailure()
        {
            CreateLauncher();
            SetProbe("failStart", true);
            Assert.IsFalse(StartTracker());

            Invoke("StopTracker");

            Assert.IsEmpty(LastError);
            Assert.AreEqual("캠 켜기", label.text);
            Assert.AreEqual(0, GetProbe<int>("stopCalls"));
        }

        private string LastError => (string)AssertPublicProperty("LastError", typeof(string)).GetValue(launcher);

        private void CreateLauncher()
        {
            Type probeType = typeof(HandInteractionProbe).Assembly.GetType("CameraCoop.Tests.TrackerLauncherProbe");
            Assert.IsNotNull(probeType, "TrackerLauncher requires a runtime process probe so tests never start Python.");
            rig = new GameObject("tracker launcher test");
            rig.SetActive(false);
            launcher = (TrackerLauncher)rig.AddComponent(probeType);
            var labelObject = new GameObject("legacy tracker label", typeof(RectTransform), typeof(Text));
            labelObject.transform.SetParent(rig.transform, false);
            label = labelObject.GetComponent<Text>();
            typeof(TrackerLauncher).GetField("buttonLabel", InstanceFlags).SetValue(launcher, label);
        }

        private bool StartTracker()
        {
            return (bool)AssertPublicMethod("StartTracker", typeof(bool)).Invoke(launcher, null);
        }

        private void Invoke(string name)
        {
            MethodInfo method = typeof(TrackerLauncher).GetMethod(name, InstanceFlags);
            Assert.IsNotNull(method, "TrackerLauncher must implement " + name + ".");
            method.Invoke(launcher, null);
        }

        private void SetProbe(string name, object value)
        {
            FieldInfo field = launcher.GetType().GetField(name);
            Assert.IsNotNull(field, "TrackerLauncherProbe must expose " + name + ".");
            field.SetValue(launcher, value);
        }

        private T GetProbe<T>(string name)
        {
            return (T)launcher.GetType().GetField(name).GetValue(launcher);
        }

        private static MethodInfo AssertPublicMethod(string name, Type returnType)
        {
            MethodInfo method = typeof(TrackerLauncher).GetMethod(name, BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(method, "TrackerLauncher must expose public " + name + ".");
            Assert.AreEqual(returnType, method.ReturnType);
            return method;
        }

        private static PropertyInfo AssertPublicProperty(string name, Type propertyType)
        {
            PropertyInfo property = typeof(TrackerLauncher).GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(property, "TrackerLauncher must expose public " + name + ".");
            Assert.AreEqual(propertyType, property.PropertyType);
            return property;
        }
    }
}
