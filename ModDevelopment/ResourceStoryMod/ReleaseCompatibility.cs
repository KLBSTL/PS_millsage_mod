using System;
using System.IO;

namespace ResourceStoryMod
{
    // Core.Test is always null in the public build. Keep the small contracts
    // used by shared gameplay code while omitting the local acceptance drivers.
    internal sealed class TestSession
    {
        private TestSession() { }

        internal static string Hash(string path) => RuntimeCompat.Hex(RuntimeCompat.Sha256(File.ReadAllBytes(path)));
        internal static bool LocalizationReady() => false;

        internal string PackagePath => null;
        internal string SaveRoot => null;
        internal string SessionId => null;
        internal string Stage => null;
        internal TestRequest Request => null;
        internal NativeTestTools Tools => null;
        internal string ForcedVisitActor => null;
        internal bool Campaign => false;
        internal bool CampaignDriverReady => false;
        internal bool Delivery => false;
        internal bool Headless => false;
        internal bool NaturalSampleViolated { get; set; }
        internal bool NaturalSamplingActive => false;
        internal bool OperatingTrialActive => false;
        internal bool PackageA => false;
        internal bool Preview => false;
        internal bool ReadyPolling => false;
        internal bool SuppressStory => false;
        internal bool ToolsMode => false;
        internal bool Wallet => false;
        internal bool Workshop => false;

        internal void Draw() { }
        internal void Fail(Exception error) { }
        internal void RestoreEnvironment() { }
        internal void Update() { }
    }

    internal sealed class TestRequest
    {
        internal string InspectionStage => "";
        internal string Mode => "";
        internal string Native012 => "";
        internal string OpeningMode => "";
        internal bool ActOneExtension => false;
        internal bool ActTwoExtension => false;
        internal bool AutoRun => false;
        internal bool CampaignComplete => false;
        internal bool CampaignItems => false;
        internal bool DiaryExtension => false;
        internal bool NormalPlay => false;
        internal bool PackageD => false;
        internal bool RepairNative => false;
        internal bool ReturnExtension => false;
    }

    internal sealed class NativeTestTools
    {
        internal bool P18QueueReady => false;
        internal bool PanelOpen => false;
        internal void LateOpeningCapture() { }
        internal static void Atomic(string path, string value) { }
    }
}
