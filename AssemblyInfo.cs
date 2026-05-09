using System.Reflection;
using SecRandom;

[assembly: AssemblyVersion(GitInfo.Tag)]
[assembly: AssemblyInformationalVersion($"{GitInfo.Tag}+{GitInfo.CommitHash}")]
[assembly: AssemblyTitle("SecRandom")]
[assembly: AssemblyProduct("SecRandom")]

#if NETCOREAPP
// [assembly: SupportedOSPlatform("Windows")]
#endif
#if Platforms_MacOs
[assembly:SupportedOSPlatform("macos")]
#endif