using System.Runtime.CompilerServices;
using Avalonia.Metadata;

[assembly: InternalsVisibleTo("ClassIsland")]
[assembly: InternalsVisibleTo("ClassIsland.Desktop")]
[assembly: InternalsVisibleTo("ClassIsland.Platforms.Windows")]
[assembly: InternalsVisibleTo("SecRandom.Core.Tests")]
[assembly: InternalsVisibleTo("SecRandom.FairnessAudit")]
[assembly: InternalsVisibleTo("SecRandom")]

[assembly: XmlnsPrefix("http://secrandom.sectl.cn/schemas/xaml/core", "sr")]
[assembly: XmlnsDefinition("http://secrandom.sectl.cn/schemas/xaml/core", "SecRandom.Core")]
[assembly: XmlnsDefinition("http://secrandom.sectl.cn/schemas/xaml/core", "SecRandom.Core.Abstraction.Controls")]
[assembly: XmlnsDefinition("http://secrandom.sectl.cn/schemas/xaml/core", "SecRandom.Core.Behaviors")]
[assembly: XmlnsDefinition("http://secrandom.sectl.cn/schemas/xaml/core", "SecRandom.Core.Controls")]
[assembly: XmlnsDefinition("http://secrandom.sectl.cn/schemas/xaml/core", "SecRandom.Core.Converters")]
[assembly: XmlnsDefinition("http://secrandom.sectl.cn/schemas/xaml/core", "SecRandom.Core.MarkupExtensions")]
