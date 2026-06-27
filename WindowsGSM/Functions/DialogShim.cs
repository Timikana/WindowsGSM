namespace WindowsGSM
{
    // Types de dialog "maison" qui remplacent MahApps.Metro.Controls.Dialogs
    // (permet de supprimer toute dépendance MahApps). Mêmes noms/membres que MahApps
    // pour que les call sites existants compilent sans changement.
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
