The game now reads files from the StreamingAssets folder, not the asset bundles.
Don't ask me why. 
Hint : 
string dirRoot = Path.Combine(Application.dataPath, "StreamingAssets", "Language_" + LocalStringManager.CurLanguageKey);
		string[] filePaths = Directory.GetFiles(dirRoot, "*.txt", SearchOption.AllDirectories);

  Gotta go to bed
