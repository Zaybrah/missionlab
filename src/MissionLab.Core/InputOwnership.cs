namespace MissionLab.Core
{
    /// <summary>What a release asks the game side to give back; each is true only if it was actually held.</summary>
    public struct ReleaseOrder
    {
        /// <summary>The free camera was opened by the mod and must be switched off again.</summary>
        public bool Camera;
        /// <summary>The game's keys were switched off by the mod and must be switched back on.</summary>
        public bool Input;
        /// <summary>A placed marker was still following the crosshair; that placing is cancelled.</summary>
        public bool Aiming;
        /// <summary>The mode itself was open.</summary>
        public bool Mode;
    }

    /// <summary>
    /// Everything the Game Master holds in the game, in one place, so that one release gives all of it back.
    /// Before this the mode could stay marked open after a failure and the key hook went on swallowing the game's keys.
    /// Kept free of Unity so the rules can be tested; the Director does the actual camera and key calls.
    /// After a release only an explicit <see cref="Open"/> (the host pressing the Game Master key) takes control again:
    /// the rules recovering from an error must never quietly grab the host's keys.
    /// </summary>
    public sealed class InputOwnership
    {
        /// <summary>The Game Master mode is open.</summary>
        public bool IsOpen { get; private set; }
        /// <summary>The game's own keys are switched off by the mod.</summary>
        public bool InputTaken { get; private set; }
        /// <summary>The free camera is on because the mod switched it on.</summary>
        public bool CameraOurs { get; private set; }
        /// <summary>A placed marker follows the crosshair until the second click.</summary>
        public bool Aiming { get; private set; }
        /// <summary>Why control was last given back ("" if it never was).</summary>
        public string LastReason { get; private set; } = "";
        /// <summary>How many releases actually gave something back.</summary>
        public int Releases { get; private set; }

        /// <summary>The host opened the mode on purpose.</summary>
        public void Open()
        {
            IsOpen = true;
        }

        /// <summary>
        /// The mode wants the game's keys this frame. Refused unless the mode is open, so a tick after a release
        /// (for example when the rules are tried again) cannot take them back.
        /// </summary>
        public bool TakeInput(bool blockSetting)
        {
            if (!IsOpen || !blockSetting) return false;
            InputTaken = true;
            return true;
        }

        /// <summary>The free camera's state after the mode switched it; only an open mode can own it.</summary>
        public void CameraSwitched(bool on)
        {
            CameraOurs = on && IsOpen;
        }

        /// <summary>A marker started or stopped following the crosshair.</summary>
        public void SetAiming(bool on)
        {
            Aiming = on && IsOpen;
        }

        /// <summary>Whether the key hook must swallow the game's keys now.</summary>
        public bool BlocksGameKeys(bool blockSetting)
        {
            return IsOpen && blockSetting;
        }

        /// <summary>
        /// Gives back everything at once and closes the mode. Harmless when nothing is held: the order is then empty
        /// and the reason of the last real release is kept.
        /// </summary>
        public ReleaseOrder Release(string reason)
        {
            var order = new ReleaseOrder { Camera = CameraOurs, Input = InputTaken, Aiming = Aiming, Mode = IsOpen };
            IsOpen = false; InputTaken = false; CameraOurs = false; Aiming = false;
            if (order.Camera || order.Input || order.Aiming || order.Mode)
            {
                LastReason = reason ?? "";
                Releases++;
            }
            return order;
        }
    }
}
