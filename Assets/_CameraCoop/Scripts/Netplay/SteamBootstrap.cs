using System;
using UnityEngine;

namespace CameraCoop.Netplay
{
    // SteamClient 수명 관리 (docs/08 §7). 개발 AppID 480. 출시 시 AppID만 교체.
    public static class SteamBootstrap
    {
        public const uint DevAppId = 480;

        public static bool IsValid { get { return Steamworks.SteamClient.IsValid; } }
        public static string LocalSteamId { get { return Steamworks.SteamClient.SteamId.ToString(); } }
        public static string LocalName { get { return Steamworks.SteamClient.Name; } }

        public static bool TryInit()
        {
            if (Steamworks.SteamClient.IsValid)
            {
                return true;
            }
            try
            {
                Steamworks.SteamClient.Init(DevAppId, asyncCallbacks: true);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SteamBootstrap] Steam init 실패 (클라이언트 미실행/미로그인?): " + e.Message);
                return false;
            }
        }

        public static void Shutdown()
        {
            if (Steamworks.SteamClient.IsValid)
            {
                Steamworks.SteamClient.Shutdown();
            }
        }

        // play 종료/앱 종료 시 native Steam 정리 — asyncCallbacks 펌프가 domain reload를 넘기지 않게 (최종 리뷰 I-4)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void HookQuitting()
        {
            Application.quitting -= Shutdown;
            Application.quitting += Shutdown;
        }
    }
}
