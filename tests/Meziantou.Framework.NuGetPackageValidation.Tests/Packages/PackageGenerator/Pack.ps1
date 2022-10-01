# https://docs.microsoft.com/en-us/nuget/reference/msbuild-targets#pack-target
$OutputFolder = Join-Path $PSScriptRoot ".." -Resolve
$ProjectFile = Join-Path $PSScriptRoot "samplelib.csproj" -Resolve

function Clear-Binaries(){
	$BinFolder = Join-Path $PSScriptRoot "bin"
	$ObjFolder = Join-Path $PSScriptRoot "obj"
	Remove-Item $BinFolder -Recurse -Force -ErrorAction SilentlyContinue
	Remove-Item $ObjFolder -Recurse -Force -ErrorAction SilentlyContinue
}

Clear-Binaries
dotnet pack $ProjectFile --output $OutputFolder /p:PackageId=Debug
dotnet pack $ProjectFile --output $OutputFolder /p:PackageId=Release --configuration Release
dotnet pack $ProjectFile --output $OutputFolder /p:PackageId=Release_Author --configuration Release /p:Authors="Sample"
dotnet pack $ProjectFile --output $OutputFolder /p:PackageId=Release_DefaultAuthor --configuration Release /p:Authors="Release_DefaultAuthor"
dotnet pack $ProjectFile --output $OutputFolder /p:PackageId=Release_Description --configuration Release /p:Description="Sample desc"
dotnet pack $ProjectFile --output $OutputFolder /p:PackageId=Release_Readme --configuration Release /p:PackageReadmeFile="README.md"
dotnet pack $ProjectFile --output $OutputFolder /p:PackageId=Release_Icon --configuration Release /p:PackageIcon="icon.png"
dotnet pack $ProjectFile --output $OutputFolder /p:PackageId=Release_IconUrl --configuration Release /p:PackageIconUrl="https://www.example.com/image.png"
dotnet pack $ProjectFile --output $OutputFolder /p:PackageId=Release_Icon_IconUrl --configuration Release /p:PackageIcon="icon.png" /p:PackageIconUrl="https://www.example.com/image.png"
dotnet pack $ProjectFile --output $OutputFolder /p:PackageId=Release_Icon_WrongExtension --configuration Release /p:PackageIcon="icon_wrongextension.jpg"
dotnet pack $ProjectFile --output $OutputFolder /p:PackageId=Release_LicenseExpression --configuration Release /p:PackageLicenseExpression="MIT"
dotnet pack $ProjectFile --output $OutputFolder /p:PackageId=Release_License --configuration Release /p:PackageLicenseFile="LICENSE.txt"
dotnet pack $ProjectFile --output $OutputFolder /p:PackageId=Release_LicenseUrl --configuration Release /p:PackageLicenseUrl="https://www.example.com/license.txt"
dotnet pack $ProjectFile --output $OutputFolder /p:PackageId=Release_RepositoryType --configuration Release /p:RepositoryType="git"
dotnet pack $ProjectFile --output $OutputFolder /p:PackageId=Release_RepositoryType_RepositoryUrl_RepositoryCommit --configuration Release /p:RepositoryType="git" /p:RepositoryUrl="https://repo" /p:RepositoryCommit="123"
dotnet pack $ProjectFile --output $OutputFolder /p:PackageId=Release_RepositoryType_RepositoryUrl_RepositoryCommit_RepositoryBranch --configuration Release /p:RepositoryType="git" /p:RepositoryUrl="https://repo" /p:RepositoryCommit="123" /p:RepositoryBranch="main"

Clear-Binaries
dotnet pack $ProjectFile --output $OutputFolder /p:PackageId=Release_XmlDocumentation --configuration Release /p:GenerateDocumentationFile=true

Clear-Binaries
dotnet pack $ProjectFile --output $OutputFolder /p:PackageId=Release_Deterministic_Embedded_SourcesNotEmbedded --configuration Release /p:Deterministic=true /p:EmbedAllSources=false /p:EmbedUntrackedSources=false /p:ContinuousIntegrationBuild=true /p:DebugType=embedded

Clear-Binaries
dotnet pack $ProjectFile --output $OutputFolder /p:PackageId=Release_Deterministic_Embedded --configuration Release /p:Deterministic=true /p:EmbedAllSources=true /p:ContinuousIntegrationBuild=true /p:DebugType=embedded

Clear-Binaries
dotnet pack $ProjectFile --output $OutputFolder /p:PackageId=Release_Deterministic_Snupkg --configuration Release /p:Deterministic=true /p:EmbedAllSources=true /p:EmbedUntrackedSources=true /p:ContinuousIntegrationBuild=true /p:IncludeSymbols=true /p:SymbolPackageFormat=snupkg

Clear-Binaries
dotnet pack $ProjectFile --output $OutputFolder /p:PackageId=Release_Deterministic_Pdb --configuration Release /p:Deterministic=true /p:EmbedAllSources=true /p:EmbedUntrackedSources=true /p:ContinuousIntegrationBuild=true /p:IncludePDB=true

Clear-Binaries
dotnet pack $ProjectFile --output $OutputFolder /p:PackageId=Release_NonDeterministic_Pdb --configuration Release /p:Deterministic=false /p:EmbedAllSources=true /p:EmbedUntrackedSources=true /p:ContinuousIntegrationBuild=false /p:IncludePDB=true

Clear-Binaries
dotnet pack $ProjectFile --output $OutputFolder /p:PackageId=Release_Deterministic_Pdb_Full --configuration Release /p:Deterministic=true /p:EmbedAllSources=true /p:EmbedUntrackedSources=true /p:ContinuousIntegrationBuild=true /p:DebugType=full /p:IncludePDB=true

Clear-Binaries
dotnet pack $ProjectFile --output $OutputFolder /p:PackageId=Release_Deterministic_Pdb_Full_Net47 /p:TargetNet47=true --configuration Release /p:Deterministic=true /p:EmbedAllSources=true /p:EmbedUntrackedSources=true /p:ContinuousIntegrationBuild=true /p:DebugType=full /p:IncludePDB=true

Clear-Binaries