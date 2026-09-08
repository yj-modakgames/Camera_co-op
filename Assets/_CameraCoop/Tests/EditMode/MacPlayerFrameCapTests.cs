using NUnit.Framework;
using UnityEngine;

namespace CameraCoop.Tests
{
    public class MacPlayerFrameCapTests
    {
        [Test]
        public void ShouldApply_OSXPlayerNotEditor_ReturnsTrue()
        {
            Assert.IsTrue(MacPlayerFrameCap.ShouldApply(RuntimePlatform.OSXPlayer, false));
        }

        [Test]
        public void ShouldApply_WindowsPlayer_ReturnsFalse()
        {
            Assert.IsFalse(MacPlayerFrameCap.ShouldApply(RuntimePlatform.WindowsPlayer, false));
        }

        [Test]
        public void ShouldApply_OSXPlayerButIsEditor_ReturnsFalse()
        {
            Assert.IsFalse(MacPlayerFrameCap.ShouldApply(RuntimePlatform.OSXPlayer, true));
        }

        [Test]
        public void ShouldApply_OSXEditor_ReturnsFalse()
        {
            Assert.IsFalse(MacPlayerFrameCap.ShouldApply(RuntimePlatform.OSXEditor, false));
        }
    }
}
