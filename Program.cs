using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.Syntax;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.Semantics;
using ICSharpCode.Decompiler.TypeSystem;
using Kaitai;
using Newtonsoft.Json;
using YamlDotNet.Serialization;
using static System.Net.Mime.MediaTypeNames;
using static Kaitai.UnityBundle.BlockInfoAndDirectoryT;

const string OUTPUT_DIR = "zh-hans";

var cliArgs = Environment.GetCommandLineArgs();
if (cliArgs.Length != 2)
{
  Console.Error.WriteLine("Error: game directory is missing, pass game directory as the first argument!");
  Environment.Exit(1);
}
var gameDirectory = cliArgs[1];
if (!Directory.Exists(gameDirectory))
{
  Console.Error.WriteLine($"Invalid game directory: {gameDirectory}!");
  Environment.Exit(1);
}

var managedAssembly = Path.Join(gameDirectory, "The Scroll of Taiwu_Data", "Managed", "Assembly-CSharp.dll");
if (!File.Exists(managedAssembly))
{
  Console.Error.WriteLine($"Invalid managed assembly: {managedAssembly}!");
  Environment.Exit(1);
}

var module = new PEFile(managedAssembly);
var resolver = new UniversalAssemblyResolver(managedAssembly, false, module.DetectTargetFrameworkId());

var settings = new DecompilerSettings(LanguageVersion.Latest)
{
  ThrowOnAssemblyResolveErrors = true,
};
var decompiler = new CSharpDecompiler(managedAssembly, resolver, settings);
var fullTypeName = new FullTypeName("LanguageKey");
var ast = decompiler.DecompileType(fullTypeName);

var typeDeclaration = (TypeDeclaration)ast.Children.First(node => node as TypeDeclaration != null);
var fieldDeclaration = (FieldDeclaration)typeDeclaration.Children.First(node => node is FieldDeclaration && (((FieldDeclaration)node).Modifiers & Modifiers.Private) != 0);
var variableInitializer = (VariableInitializer)fieldDeclaration.Children.First(node => node is VariableInitializer);
var objectCreateExpression = (ObjectCreateExpression)variableInitializer.Children.First(node => node is ObjectCreateExpression);
var arrayInitializer = (ArrayInitializerExpression)objectCreateExpression.Children.First(node => node is ArrayInitializerExpression);

var languageKeyToLineMapping = arrayInitializer.Children.Aggregate(new Dictionary<string, int>(), (acc, node) =>
{
  if (!(node is ArrayInitializerExpression)) throw new Exception("invalid array node");
  var arrayNode = (ArrayInitializerExpression)node;

  if (!(arrayNode.FirstChild is PrimitiveExpression)) throw new Exception("invalid key node");
  var keyNode = (PrimitiveExpression)arrayNode.FirstChild;
  var key = (string)keyNode.Value;

  if (!(arrayNode.LastChild is PrimitiveExpression)) throw new Exception("invalid val node");
  var valNode = (PrimitiveExpression)arrayNode.LastChild;
  var val = (int)valNode.Value;

  acc[key] = val;

  return acc;
});
var eventsDirectory = Path.Join(gameDirectory, "Event", "EventLanguages");
if (!Directory.Exists(eventsDirectory))
{
    Console.Error.WriteLine($"Invalid events directory: {eventsDirectory}!");
    Environment.Exit(1);
}

Console.WriteLine("[+] saving EventLanguages...");

DirectoryInfo d = new DirectoryInfo(eventsDirectory); //Assuming Test is your Folder
Console.WriteLine("Loading files");
Dictionary<string, FileInfo> files = d.GetFiles("*.txt").ToDictionary(file => file.Name); //Getting Text files
Console.WriteLine("Generating Templates");
//Console.WriteLine("Debug 1");
//Console.WriteLine("file count : " + files.Count);
Dictionary<string, TaiWuTemplate> parsedTemplates = new Dictionary<string, TaiWuTemplate> ();
foreach(var i in files)
{
    try
    {
        parsedTemplates.Add(i.Key, new TaiWuTemplate(i.Value));
    }
    catch
    {
        Console.Write(Environment.NewLine);
        Console.WriteLine("There's an issue with file " + i.Key + " in your " + @"Steam\steamapps\common\The Scroll of Taiwu\Event\EventLanguages folder. 
Please check that it's not empty. It should contain fields such as EventGuid, EventContent or Option_1, 2 etc. 
If it only has stuff like - Group : InjectionInteractOption
- GroupName : xxxxxxxxxxx
- Language : CN

Then it's most probably not containing text that's interesting to you. A stub, if you may. So let's carry one. If it's not the case, call Cadenza, we'll figure it out.");
        Console.Write(Environment.NewLine);
    }
}
//Dictionary<string, TaiWuTemplate> parsedTemplates = files.ToDictionary(f => f.Key, f => new TaiWuTemplate(f.Value));
Dictionary<string, string> flatDict = parsedTemplates.Values
    .ToList()
    .SelectMany(template => template.FlattenTemplateToDict())
    .ToDictionary(pair => pair.Key, pair => pair.Value);
File.WriteAllText(Path.Join(OUTPUT_DIR, "events.json"), JsonConvert.SerializeObject(flatDict, Formatting.Indented));

var languageCnAssetBundle = Path.Join(gameDirectory, "The Scroll of Taiwu_Data", "GameResources", "language_cn.uab");
if (!File.Exists(languageCnAssetBundle))
{
  Console.Error.WriteLine($"Invalid language_cn.uab: {languageCnAssetBundle}!");
  Environment.Exit(1);
}



string langDir = Path.Combine(gameDirectory, "The Scroll of Taiwu_Data", "StreamingAssets", "Language_CN");
if (!Directory.Exists(langDir))
{
    Console.Error.WriteLine($"Could not find StreamingAssets language folder: {langDir}!");
    Environment.Exit(1);
}

Console.WriteLine("[+] Processing StreamingAssets .txt files...");

foreach (var filePath in Directory.EnumerateFiles(langDir, "*.txt", SearchOption.TopDirectoryOnly))
{
    var fileName = Path.GetFileNameWithoutExtension(filePath); // e.g., "ui_language"

    string text = File.ReadAllText(filePath);
    string[] lines = text.Split('\n'); // Use game-like logic
    lines = lines.Select(line => line.Replace("\\n", "\n")).ToArray();

    Console.WriteLine($"[+] Saving {fileName}...");

    if (fileName == "ui_language")
    {
        var entries = languageKeyToLineMapping.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value < lines.Length ? lines[kvp.Value].Trim() : $"<INVALID_INDEX_{kvp.Value}>"
        );

        File.WriteAllText(Path.Combine(OUTPUT_DIR, "ui_language.json"),
            JsonConvert.SerializeObject(entries, Formatting.Indented));
    }
    else if (fileName == "Adventure_language")
    {
        var jsonlines = lines.Where(x => x.Contains("LK_") && x.Contains("="));
        var dict = new Dictionary<string, string>();

        foreach (var l in jsonlines)
        {
            var parts = l.Split('=');
            if (parts.Length == 2)
                dict[parts[0]] = parts[1];
        }

        File.WriteAllText(Path.Combine(OUTPUT_DIR, $"{fileName}.json"),
            JsonConvert.SerializeObject(dict, Formatting.Indented));

        var sb = new System.Text.StringBuilder();
        foreach (var line in lines)
        {
            if (!jsonlines.Contains(line) && !line.Contains(">>>>>>>>>>>>>>>>>>"))
                sb.AppendLine(line);
        }

        File.WriteAllText(Path.Combine(OUTPUT_DIR, $"{fileName}.txt"), sb.ToString());
    }
    else
    {
        File.WriteAllText(Path.Combine(OUTPUT_DIR, $"{fileName}.txt"), text);
    }
}









