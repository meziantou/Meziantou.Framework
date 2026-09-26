using System.Runtime.CompilerServices;

// InternalsVisibleTo is not compatible with strong-name signed assemblies unless the
// friend assembly is also signed with a known public key. Exclude it for the signed build.
[assembly: InternalsVisibleTo("Tomlyn.Tests")]
