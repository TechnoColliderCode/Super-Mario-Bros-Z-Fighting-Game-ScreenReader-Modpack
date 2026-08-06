using System;
using System.Collections.Generic;
using SMBZG;
using TeamUtility.IO;
using TMPro;
using UnityEngine;

namespace SMBZG.Accessibility
{
    /// <summary>
    /// Narrates the fight: battle begin, pause/resume, attacks, hits, blocks, combos,
    /// rushes, stuns, KO's, plus periodic status/spatial readouts and on-demand info.
    /// </summary>
    internal static class BattleAnnouncer
    {
        private sealed class PlayerState
        {
            public CharacterControl Control;
            public PlayerID Index;
            public bool WasDead;
            public bool WasGuarding;
            public float LastHealthPct = -1f;
            public float LastEnergyPct = -1f;
            public float LastStun = -1f;
            public float LastCombo;
            public bool AnnouncedLowHealth25;
            public bool AnnouncedLowHealth10;
            public bool AnnouncedLowHealth5;
            public bool AnnouncedFullEnergy;
            public bool AnnouncedStunned;
            public string CharacterName = "Character";
        }

        private static PlayerState _p1;
        private static PlayerState _p2;
        private static bool _staticEventsSubscribed;
        private static bool _inBattle;
        private static bool _battleBegun;

        private static float _nextPollTime;
        private static float _nextStatusTime;
        private static float _nextSpatialTime;
        private static float _nextTimerTime;
        private static RoundTimerUI _timerUI;
        private static string _lastTimerText;
        private static float _resultsAnnounceAt = -1f;
        private static bool _resultsHandled;
        private static readonly HashSet<CharacterControl> _subscribedControls = new HashSet<CharacterControl>();

        public static void Initialize()
        {
            if (_staticEventsSubscribed)
            {
                return;
            }
            _staticEventsSubscribed = true;
            BattleController.OnBattleBegin += OnBattleBegin;
            BattleController.OnPause_Event += OnPause;
            BattleController.OnUnpause_Event += OnUnpause;
            BattleController.OnComboStart_Event += OnComboStart;
            BattleController.OnComboEnd_Event += OnComboEnd;
            BattleController.OnRushStart_Event += OnRushStart;
            BattleController.OnRushEnd_Event += OnRushEnd;
        }

        public static void OnSceneChanged(string sceneName)
        {
            _p1 = null;
            _p2 = null;
            _subscribedControls.Clear();
            _timerUI = null;
            _lastTimerText = null;
            _battleBegun = false;
            _resultsHandled = false;
            _resultsAnnounceAt = -1f;

            _inBattle = sceneName == SceneConstants.Battle;
            if (_inBattle)
            {
                _nextPollTime = 0f;
                _nextStatusTime = Time.realtimeSinceStartup + 6f;
                _nextSpatialTime = Time.realtimeSinceStartup + Settings.PeriodicSpatialInterval;
                _nextTimerTime = 0f;
            }
            else if (sceneName == SceneConstants.BattleResults || sceneName == SceneConstants.BattleResults_ArcadeReview)
            {
                _resultsAnnounceAt = Time.realtimeSinceStartup + 1.5f;
            }
        }

        public static void Update()
        {
            if (_resultsAnnounceAt > 0f && Time.realtimeSinceStartup >= _resultsAnnounceAt && !_resultsHandled)
            {
                _resultsHandled = true;
                _resultsAnnounceAt = -1f;
                AnnounceResults();
            }
            if (!_inBattle)
            {
                return;
            }
            if (!_battleBegun)
            {
                return;
            }
            AcquirePlayers();
            if (_p1 == null || _p2 == null)
            {
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (now < _nextPollTime)
            {
                return;
            }
            _nextPollTime = now + 0.25f;

            PollPlayer(_p1);
            PollPlayer(_p2);

            if (Settings.PeriodicStatus && now >= _nextStatusTime)
            {
                _nextStatusTime = now + Settings.PeriodicStatusInterval;
                Speaker.Speak(BuildStatusText(), "status", SpeechPriority.Status, 0.2f);
            }
            if (Settings.PeriodicSpatial && now >= _nextSpatialTime)
            {
                _nextSpatialTime = now + Settings.PeriodicSpatialInterval;
                Speaker.Speak(BuildSpatialText(), "spatial", SpeechPriority.Spatial, 0.2f);
            }
            if (now >= _nextTimerTime)
            {
                _nextTimerTime = now + 1f;
                string t = GetTimerText();
                if (!string.IsNullOrEmpty(t) && t != _lastTimerText)
                {
                    _lastTimerText = t;
                }
            }
        }

        private static void AcquirePlayers()
        {
            BattleController bc = BattleController.instance;
            if (bc == null)
            {
                return;
            }
            if (_p1 == null || _p1.Control == null)
            {
                CharacterControl c1 = bc.GetPlayer(PlayerID.One);
                if (c1 != null)
                {
                    _p1 = new PlayerState { Index = PlayerID.One };
                    Attach(_p1, c1);
                }
            }
            if (_p2 == null || _p2.Control == null)
            {
                CharacterControl c2 = bc.GetPlayer(PlayerID.Two);
                if (c2 != null)
                {
                    _p2 = new PlayerState { Index = PlayerID.Two };
                    Attach(_p2, c2);
                }
            }
        }

        private static void Attach(PlayerState state, CharacterControl control)
        {
            state.Control = control;
            if (!_subscribedControls.Add(control))
            {
                return;
            }
            control.OnHitStunStart += () => OnPlayerHit(state);
            control.OnHitStunEnd += () => { };
            control.OnBlockStunStart += () => OnPlayerBlockHit(state);
            control.OnBlockStunEnd += () => { };
            control.OnAttackingStart += () => OnPlayerAttackStart(state);
            control.OnAttackingEnd += () => { };
            state.CharacterName = GetCharacterName(state);
        }

        // ---- Static event handlers ----

        private static void OnBattleBegin()
        {
            _battleBegun = true;
            AcquirePlayers();
            string desc = "Fight!";
            BattleController bc = BattleController.instance;
            if (bc != null && bc.Text_RoundDescription != null)
            {
                desc = bc.Text_RoundDescription.text + ". Fight!";
            }
            Speaker.InterruptNow(desc, "battleevent");
            if (_p1 != null) { _p1.LastHealthPct = -1f; _p1.LastEnergyPct = -1f; _p1.LastStun = -1f; }
            if (_p2 != null) { _p2.LastHealthPct = -1f; _p2.LastEnergyPct = -1f; _p2.LastStun = -1f; }
        }

        private static void OnPause()
        {
            Speaker.InterruptNow("Paused.", "battleevent");
        }

        private static void OnUnpause()
        {
            Speaker.InterruptNow("Resumed.", "battleevent");
        }

        private static void OnComboStart(CharacterControl player)
        {
            if (!Settings.AnnounceBattleEvents)
            {
                return;
            }
            Speaker.Speak(PlayerLabel(player) + " combo.", "battleevent", SpeechPriority.BattleEvent, 1.2f);
        }

        private static void OnComboEnd(CharacterControl player)
        {
            // combo counters reset handled by polling
        }

        private static void OnRushStart(CharacterControl player)
        {
            if (!Settings.AnnounceBattleEvents)
            {
                return;
            }
            Speaker.Speak("Movement rush!", "battleevent", SpeechPriority.BattleEvent, 2f);
        }

        private static void OnRushEnd(CharacterControl player)
        {
            if (!Settings.AnnounceBattleEvents)
            {
                return;
            }
            Speaker.Speak("Rush over.", "battleevent", SpeechPriority.BattleEvent, 1f);
        }

        // ---- Per-player event handlers ----

        private static void OnPlayerHit(PlayerState state)
        {
            if (!Settings.AnnounceBattleEvents || state.Control == null || state.Control.CharacterGO == null)
            {
                return;
            }
            Speaker.Speak(PlayerLabel(state) + " hit.", "hit" + (int)state.Index, SpeechPriority.BattleEvent, 0.45f);
        }

        private static void OnPlayerBlockHit(PlayerState state)
        {
            if (!Settings.AnnounceBattleEvents)
            {
                return;
            }
            Speaker.Speak(PlayerLabel(state) + " guard hit.", "block" + (int)state.Index, SpeechPriority.BattleEvent, 0.5f);
        }

        private static void OnPlayerAttackStart(PlayerState state)
        {
            if (!Settings.AnnounceBattleEvents)
            {
                return;
            }
            string extra = string.Empty;
            BaseCharacter go = state.Control != null ? state.Control.CharacterGO : null;
            if (go != null && go.CurrentAttackData != null && !string.IsNullOrEmpty(go.CurrentAttackData.AttackName))
            {
                string n = go.CurrentAttackData.AttackName;
                if (n.Length <= 24)
                {
                    extra = " " + n;
                }
            }
            Speaker.Speak(PlayerLabel(state) + " attacks." + extra, "attack" + (int)state.Index, SpeechPriority.BattleEvent, 0.4f);
        }

        // ---- Polling ----

        private static void PollPlayer(PlayerState state)
        {
            if (state.Control == null)
            {
                return;
            }
            PlayerBattleDataModel data = state.Control.PlayerDataReference;
            BaseCharacter go = state.Control.CharacterGO;
            if (data == null)
            {
                return;
            }

            float healthPct = data.Health.Max > 0f ? (data.Health.GetCurrent() / data.Health.Max) * 100f : 0f;
            float energyPct = data.Energy.Max > 0f ? (data.Energy.GetCurrent() / data.Energy.Max) * 100f : 0f;
            float stun = data.Stun.GetCurrent();
            float combo = data.CurrentComboCount;
            float damage = data.CurrentDamageCount;
            bool dead = go != null && go.IsDead;

            if (!state.WasDead && dead)
            {
                state.WasDead = true;
                Speaker.Speak(PlayerLabel(state) + " defeated.", "death" + (int)state.Index, SpeechPriority.BattleEvent, 0f);
            }
            state.WasDead = dead;

            if (state.LastHealthPct < 0f)
            {
                state.LastHealthPct = healthPct;
            }
            if (state.LastEnergyPct < 0f)
            {
                state.LastEnergyPct = energyPct;
            }
            if (state.LastStun < 0f)
            {
                state.LastStun = stun;
            }

            float healthDrop = state.LastHealthPct - healthPct;
            if (healthDrop >= 5f && !dead)
            {
                Speaker.Speak(PlayerLabel(state) + " health " + Mathf.RoundToInt(healthPct) + " percent.", "health" + (int)state.Index, SpeechPriority.Status, 1.1f);
            }
            else if (healthPct <= 5f && !state.AnnouncedLowHealth5 && state.LastHealthPct > 5f)
            {
                state.AnnouncedLowHealth5 = true;
                Speaker.Speak(PlayerLabel(state) + " at 5 percent health.", "health" + (int)state.Index, SpeechPriority.BattleEvent, 0f);
            }
            else if (healthPct <= 10f && !state.AnnouncedLowHealth10 && state.LastHealthPct > 10f)
            {
                state.AnnouncedLowHealth10 = true;
                Speaker.Speak(PlayerLabel(state) + " at 10 percent health.", "health" + (int)state.Index, SpeechPriority.BattleEvent, 0f);
            }
            else if (healthPct <= 25f && !state.AnnouncedLowHealth25 && state.LastHealthPct > 25f)
            {
                state.AnnouncedLowHealth25 = true;
                Speaker.Speak(PlayerLabel(state) + " at 25 percent health.", "health" + (int)state.Index, SpeechPriority.BattleEvent, 0f);
            }
            state.LastHealthPct = healthPct;

            if (energyPct >= 99f && !state.AnnouncedFullEnergy)
            {
                state.AnnouncedFullEnergy = true;
                Speaker.Speak(PlayerLabel(state) + " energy full.", "energy" + (int)state.Index, SpeechPriority.BattleEvent, 0f);
            }
            state.LastEnergyPct = energyPct;

            if (stun >= 95f && !state.AnnouncedStunned)
            {
                state.AnnouncedStunned = true;
                Speaker.Speak(PlayerLabel(state) + " stunned!", "stun" + (int)state.Index, SpeechPriority.BattleEvent, 0f);
            }
            state.LastStun = stun;

            if (combo >= 2f && combo > state.LastCombo)
            {
                Speaker.Speak(PlayerLabel(state) + " " + Mathf.RoundToInt(combo) + " hit combo.", "combo" + (int)state.Index, SpeechPriority.BattleEvent, 0.9f);
            }
            state.LastCombo = combo;

            if (go != null)
            {
                bool guarding = go.IsGuarding;
                if (guarding && !state.WasGuarding)
                {
                    Speaker.Speak(PlayerLabel(state) + " guarding.", "guard" + (int)state.Index, SpeechPriority.BattleEvent, 0.8f);
                }
                state.WasGuarding = guarding;
            }
        }

        // ---- On-demand readouts ----

        public static void AnnounceStatus()
        {
            if (!_inBattle)
            {
                Speaker.Speak("Not in battle.", "status", SpeechPriority.Status, 0f);
                return;
            }
            AcquirePlayers();
            if (_p1 == null || _p2 == null)
            {
                Speaker.Speak("Waiting for battle.", "status", SpeechPriority.Status, 0f);
                return;
            }
            Speaker.Speak(BuildStatusText(), "status", SpeechPriority.Status, 0f);
        }

        public static void AnnounceSpatial()
        {
            if (!_inBattle)
            {
                Speaker.Speak("Not in battle.", "spatial", SpeechPriority.Spatial, 0f);
                return;
            }
            AcquirePlayers();
            if (_p1 == null || _p2 == null || _p1.Control == null || _p2.Control == null)
            {
                Speaker.Speak("Waiting for battle.", "spatial", SpeechPriority.Spatial, 0f);
                return;
            }
            Speaker.Speak(BuildSpatialText(), "spatial", SpeechPriority.Spatial, 0f);
        }

        public static void AnnounceTimer()
        {
            if (!_inBattle)
            {
                Speaker.Speak("Not in battle.", "timer", SpeechPriority.Timer, 0f);
                return;
            }
            string t = GetTimerText();
            if (string.IsNullOrEmpty(t))
            {
                Speaker.Speak("Round timer not available yet.", "timer", SpeechPriority.Timer, 0f);
                return;
            }
            Speaker.Speak("Time " + t, "timer", SpeechPriority.Timer, 0f);
        }

        // ---- Builders ----

        private static string BuildStatusText()
        {
            return "Player 1, " + Describe(_p1) + ". Player 2, " + Describe(_p2) + ".";
        }

        private static string Describe(PlayerState state)
        {
            if (state == null || state.Control == null)
            {
                return "not ready";
            }
            PlayerBattleDataModel data = state.Control.PlayerDataReference;
            if (data == null)
            {
                return "not ready";
            }
            int health = Mathf.RoundToInt(data.Health.Max > 0f ? (data.Health.GetCurrent() / data.Health.Max) * 100f : 0f);
            int energy = Mathf.RoundToInt(data.Energy.Max > 0f ? (data.Energy.GetCurrent() / data.Energy.Max) * 100f : 0f);
            int stun = Mathf.RoundToInt(data.Stun.GetCurrent());
            return state.CharacterName + ", health " + health + ", energy " + energy + ", stun " + stun;
        }

        private static string BuildSpatialText()
        {
            if (_p1 == null || _p2 == null || _p1.Control == null || _p2.Control == null)
            {
                return "Not ready.";
            }
            Vector3 a = _p1.Control.transform.position;
            Vector3 b = _p2.Control.transform.position;
            float distance = Mathf.Abs(b.x - a.x);
            string left;
            string right;
            if (b.x >= a.x)
            {
                left = PlayerLabel(_p1);
                right = PlayerLabel(_p2);
            }
            else
            {
                left = PlayerLabel(_p2);
                right = PlayerLabel(_p1);
            }
            return left + " on the left. " + right + " on the right. Distance " + distance.ToString("0.0");
        }

        private static string GetCharacterName(PlayerState state)
        {
            if (state == null || state.Control == null)
            {
                return "Character";
            }
            StatusUIBundle ui = state.Control.StatusUI;
            if (ui != null && ui.CharacterName != null && !string.IsNullOrWhiteSpace(ui.CharacterName.text))
            {
                return ui.CharacterName.text.Trim();
            }
            PlayerBattleDataModel data = state.Control.PlayerDataReference;
            if (data != null && data.CurrentCharacterData != null)
            {
                return BattleCache.Character_GetDisplayName(data.CurrentCharacterData.Character);
            }
            return "Character";
        }

        private static string GetTimerText()
        {
            if (_timerUI == null)
            {
                _timerUI = UnityEngine.Object.FindObjectOfType<RoundTimerUI>();
            }
            if (_timerUI != null)
            {
                TMP_Text text = _timerUI.GetComponentInChildren<TMP_Text>();
                if (text != null)
                {
                    string value = text.text;
                    if (!string.IsNullOrEmpty(value))
                    {
                        return value.Replace("<size=50%>", string.Empty).Replace("</size>", string.Empty).Trim();
                    }
                }
            }
            return null;
        }

        private static void AnnounceResults()
        {
            string msg = "Battle results.";
            BattleResultsController brc = UnityEngine.Object.FindObjectOfType<BattleResultsController>();
            if (brc != null)
            {
                string v1 = brc.Text_Player1_Verdict != null ? brc.Text_Player1_Verdict.text : null;
                string v2 = brc.Text_Player2_Verdict != null ? brc.Text_Player2_Verdict.text : null;
                if (!string.IsNullOrEmpty(v1))
                {
                    msg += " Player 1, " + v1 + ".";
                }
                if (!string.IsNullOrEmpty(v2))
                {
                    msg += " Player 2, " + v2 + ".";
                }
            }
            Speaker.InterruptNow(msg, "results");
        }

        private static string PlayerLabel(PlayerState state)
        {
            return state.Index == PlayerID.One ? "Player 1" : "Player 2";
        }

        private static string PlayerLabel(CharacterControl control)
        {
            if (control == null || control.PlayerDataReference == null)
            {
                return "Player";
            }
            return control.PlayerDataReference.PlayerIndex == PlayerID.One ? "Player 1" : "Player 2";
        }
    }
}
