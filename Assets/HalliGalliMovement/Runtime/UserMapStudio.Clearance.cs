using System;
using UnityEngine;

namespace HalliGalli.Movement
{
    public sealed partial class UserMapStudio
    {
        string verifiedSnapshot, attemptSnapshot;
        CourseSession clearSession;
        bool clearAttempt;
        int clearFalls;
        string clearRunMessage = "출발점부터 종까지 혼자 클리어해야 저장할 수 있어요.";

        public bool IsClearVerified => draft != null && verifiedSnapshot == snapshot && !MapFieldsChanged && !InspectorFieldsChanged;
        public bool CanSaveVerified => CanEdit && IsClearVerified;
        public string ClearVerificationStatus => IsClearVerified
            ? "클리어 인증 완료 · 저장과 내보내기가 가능해요."
            : testing ? clearRunMessage : "클리어 미인증 · F8로 혼자 종을 울린 뒤 저장하세요.";

        void ResetDraftClearance(bool saved)
        {
            StopClearAttempt();
            verifiedSnapshot = saved && UserMapClearance.IsCleared(draft) ? snapshot : null;
        }

        void InvalidateDraftClearance()
        {
            verifiedSnapshot = null;
            StopClearAttempt();
        }

        void StopClearAttempt()
        {
            if (clearSession) clearSession.onBellRung.RemoveListener(CompleteClearAttempt);
            clearSession = null;
            clearAttempt = false;
            attemptSnapshot = null;
        }

        void PrepareClearAttempt(CourseSession game)
        {
            StopClearAttempt();
            var local = game.Local;
            foreach (var participant in game.roster.allParticipants)
                if (participant != local) participant.gameObject.SetActive(false);
            game.participants = new[] { local };
            // A clear must start at the same central departure point, never the editor camera.
            Vector3 start = new Vector3(0, .025f, CourseRoster.StartZ);
            local.standAnchor.SetPositionAndRotation(start, Quaternion.identity);
            local.retryAnchor.SetPositionAndRotation(start, Quaternion.identity);
            local.SetCheckpoint(-1, 0);
            // Falling or pressing R repeats the whole course; checkpoint chaining is not a clear.
            game.checkpoints.enabled = false;
            clearSession = game;
            attemptSnapshot = snapshot;
            clearFalls = local.Falls;
            clearAttempt = true;
            clearRunMessage = "솔로 클리어 검증 · 출발점부터 이동하여 E로 종을 울리세요.";
            game.onBellRung.AddListener(CompleteClearAttempt);
        }

        void LateUpdate()
        {
            if (!testing || busy || !clearAttempt || !clearSession) return;
            var local = clearSession.Local;
            if (local.Falls == clearFalls) return;
            clearFalls = local.Falls;
            clearRunMessage = "다시 도전 · 체크포인트 없이 출발점부터 종까지 완주하세요.";
        }

        void CompleteClearAttempt()
        {
            if (!testing || busy || !clearAttempt || CourseNetwork.Active || !clearSession
                || clearSession.participants.Length != 1 || clearSession.BellRinger != clearSession.Local
                || !clearSession.BellRung || clearSession.Local.IsRespawning
                || attemptSnapshot != snapshot || !UserMapRuntime.Current) return;
            try
            {
                if (UserMapStorage.Serialize(UserMapRuntime.Current.Data) != attemptSnapshot) return;
                UserMapClearance.RecordClear(draft);
                verifiedSnapshot = snapshot;
                clearAttempt = false;
                clearRunMessage = Status = "클리어 성공! F8로 편집에 돌아가 저장하거나 내보내세요.";
            }
            catch (Exception error)
            {
                Status = clearRunMessage = "클리어 기록을 저장하지 못했어요: " + error.Message;
            }
        }

        // Explicit diagnostic position changes must not produce a creator-clear proof.
        internal void InvalidateClearAttempt()
        {
            clearAttempt = false;
            clearRunMessage = "위치 변경으로 검증이 중단됐어요. F8로 돌아가 다시 시작하세요.";
        }

        bool RequireDraftClearance()
        {
            if (IsClearVerified) return true;
            Status = "저장하려면 F8로 출발점부터 혼자 완주하고 E로 종을 울려 주세요. 수정한 맵은 다시 클리어해야 해요.";
            return false;
        }
    }
}
