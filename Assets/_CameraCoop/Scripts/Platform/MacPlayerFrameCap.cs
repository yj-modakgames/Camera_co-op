using UnityEngine;

namespace CameraCoop
{
    // Intel Mac은 Metal이 dGPU(고전력)를 잡아 60 fps 풀 렌더에서 발열이 심하다
    // (실측 2026-09-08, MacBookPro16,1 Radeon Pro 5300M).
    // macOS Player에서만 vSync를 끄고 30 fps로 고정한다.
    // 다른 플랫폼과 Editor는 QualitySettings의 vSyncCount 1을 그대로 쓴다.
    public static class MacPlayerFrameCap
    {
        public const int MacTargetFrameRate = 30;

        public static bool ShouldApply(RuntimePlatform platform, bool isEditor)
        {
            return !isEditor && platform == RuntimePlatform.OSXPlayer;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Apply()
        {
            if (!ShouldApply(Application.platform, Application.isEditor)) return;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = MacTargetFrameRate;
            Debug.Log("[MacPlayerFrameCap] vSync 0, targetFrameRate 30");
        }
    }
}
