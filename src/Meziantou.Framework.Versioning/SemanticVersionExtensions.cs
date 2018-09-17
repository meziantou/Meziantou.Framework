namespace Meziantou.Framework.Versioning
{
    public static class SemanticVersionExtensions
    {
        public static SemanticVersion NextPatchVersion(this SemanticVersion semanticVersion)
        {
            if (semanticVersion.IsPrerelease)
            {
                return new SemanticVersion(semanticVersion.Major, semanticVersion.Minor, semanticVersion.Patch);
            }

            return new SemanticVersion(semanticVersion.Major, semanticVersion.Minor, semanticVersion.Patch + 1);
        }

        public static SemanticVersion NextMinorVersion(this SemanticVersion semanticVersion)
        {
            if (semanticVersion.IsPrerelease)
            {
                return new SemanticVersion(semanticVersion.Major, semanticVersion.Minor, 0);
            }

            return new SemanticVersion(semanticVersion.Major, semanticVersion.Minor + 1, 0);
        }

        public static SemanticVersion NextMajorVersion(this SemanticVersion semanticVersion)
        {
            if (semanticVersion.IsPrerelease)
            {
                return new SemanticVersion(semanticVersion.Major, 0, 0);
            }

            return new SemanticVersion(semanticVersion.Major + 1, 0, 0);
        }
    }
}
