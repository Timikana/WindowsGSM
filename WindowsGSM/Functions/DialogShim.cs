namespace WindowsGSM
{
    // In-house dialog types that replace MahApps.Metro.Controls.Dialogs
    // (allows removing any MahApps dependency). Same names/members as MahApps
    // so that existing call sites compile without changes.
    public enum MessageDialogResult { Affirmative, Negative, FirstAuxiliary, SecondAuxiliary }

    public enum MessageDialogStyle { Affirmative, AffirmativeAndNegative }

    public class MetroDialogSettings
    {
        public string AffirmativeButtonText { get; set; } = "OK";
        public string NegativeButtonText { get; set; } = "Cancel";
        public string DefaultText { get; set; } = "";
        public MessageDialogResult DefaultButtonFocus { get; set; } = MessageDialogResult.Affirmative;
    }
}
