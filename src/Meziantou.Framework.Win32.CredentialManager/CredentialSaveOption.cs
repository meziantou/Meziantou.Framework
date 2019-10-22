namespace Meziantou.Framework.Win32
{
    public enum CredentialSaveOption
    {
        /// <summary>The "Save credentials?" dialog box is not selected, indicating that the user doesn't want their credentials saved.</summary>
        Unselected,

        /// <summary>The "Save credentials?" dialog box is selected, indicating that the user wants their credentials saved.</summary>
        Selected,

        /// <summary>The "Save credentials?" dialog box is not displayed at all.</summary>
        Hidden,
    }
}
