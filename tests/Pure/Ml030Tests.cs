using System;
using MissionLab.Core;

/// <summary>
/// Game Master control ownership (ML-030): one release gives back the mode, the keys, the camera and any placing,
/// and only the host opening the mode again takes control back. The game's own camera and key calls are not run here.
/// </summary>
static class Ml030Tests
{
    public static void Run(Action<string, bool, string> check)
    {
        {   // An open mode with key blocking on makes the hook swallow the game's keys.
            var own = new InputOwnership();
            own.Open();
            check("an open Game Master with key blocking on blocks the game's keys", own.BlocksGameKeys(true), "");
            check("with key blocking off the game's keys still work", !own.BlocksGameKeys(false), "");
        }
        {   // A failure releases everything: keys allowed, camera and input given back, placing cancelled.
            var own = new InputOwnership();
            own.Open(); own.TakeInput(true); own.CameraSwitched(true); own.SetAiming(true);
            ReleaseOrder o = own.Release("rules paused");
            check("after the rules pause the hook lets the game's keys through", !own.BlocksGameKeys(true), "");
            check("the camera the mod opened is switched off", o.Camera, "");
            check("the keys the mod took are given back", o.Input, "");
            check("a placing in progress is cancelled", o.Aiming && !own.Aiming, "");
            check("the mode is closed and the reason kept", !own.IsOpen && own.LastReason == "rules paused", own.LastReason);
        }
        {   // The rules recovering run the mode's frame again; it must not take the keys back.
            var own = new InputOwnership();
            own.Open(); own.TakeInput(true);
            own.Release("rules paused");
            bool took = own.TakeInput(true);
            own.CameraSwitched(true);
            check("a tick after recovery does not take the keys", !took && !own.InputTaken && !own.BlocksGameKeys(true), "");
            check("a tick after recovery cannot claim the camera", !own.CameraOurs, "");
        }
        {   // The host opening the mode again on purpose works as before.
            var own = new InputOwnership();
            own.Open(); own.TakeInput(true); own.Release("rules paused");
            own.Open();
            check("the host reopening takes the keys again", own.TakeInput(true) && own.BlocksGameKeys(true), "");
        }
        {   // Releasing when nothing is held changes nothing and asks for nothing.
            var own = new InputOwnership();
            ReleaseOrder o = own.Release("nothing");
            check("a release with nothing held asks for nothing", !o.Camera && !o.Input && !o.Aiming && !o.Mode, "");
            check("a release with nothing held is not counted", own.Releases == 0 && own.LastReason == "", own.LastReason);
            own.Open(); own.Release("first"); own.Release("second");
            check("a second release keeps the reason of the real one", own.LastReason == "first" && own.Releases == 1, own.LastReason);
        }
        {   // A camera the host switched on himself, before the mode was open, is not the mod's to switch off.
            var own = new InputOwnership();
            own.CameraSwitched(true);
            own.Open();
            check("a camera switched on outside the mode is left alone", !own.Release("closed").Camera, "");
        }
        {   // Mission cleanup releases an open mode.
            var own = new InputOwnership();
            own.Open(); own.TakeInput(true); own.CameraSwitched(true);
            ReleaseOrder o = own.Release("the mission was cleaned up");
            check("mission cleanup releases the keys and the camera", o.Input && o.Camera && !own.BlocksGameKeys(true), "");
        }
    }
}
