using MEC;
using ProjectMER.Features.Objects;
using ProjectMER.Features.Serializable;
using ProjectMER.Features.Serializable.Schematics;
using Utf8Json;
using YamlDotNet.Core;

namespace ProjectMER.Features;

public static class MapUtils
{
	public const string UntitledMapName = "Untitled";

	/// <summary>分散ロードの1フレームあたり処理時間バジェット（ミリ秒）。</summary>
	public const float DefaultLoadFrameBudgetMs = 8f;

	private static readonly Dictionary<string, CoroutineHandle> StaggeredLoads = [];

	public static MapSchematic UntitledMap => LoadedMaps.GetOrAdd(UntitledMapName, () => new(UntitledMapName));

	public static Dictionary<string, MapSchematic> LoadedMaps { get; private set; } = [];

	public static void SaveMap(string mapName)
	{
		if (mapName == UntitledMapName)
			throw new InvalidOperationException("This map name is reserved for internal use!");

		bool isLoaded = LoadedMaps.TryGetValue(mapName, out MapSchematic map);
		if (isLoaded) // Map is already loaded
		{
			map.Merge(UntitledMap);
		}
		else if (TryGetMapData(mapName, out map)) // Map isn't loaded but map file exists
		{
			map.Merge(UntitledMap);
		}
		else // Map isn't loaded and map file doesn't exist
		{
			map = new MapSchematic(mapName).Merge(UntitledMap);
		}

		string path = Path.Combine(ProjectMER.MapsDir, $"{mapName}.yml");
		File.WriteAllText(path, YamlParser.Serializer.Serialize(map));
		map.IsDirty = false;

		MapSchematic untitledMap = UntitledMap;
		foreach (MapEditorObject mapEditorObject in untitledMap.SpawnedObjects)
		{
			mapEditorObject.MoveToMap(mapName);
			map.SpawnedObjects.Add(mapEditorObject);
		}

		untitledMap.SpawnedObjects.Clear();
		LoadedMaps.Remove(UntitledMapName);

		if (!isLoaded)
			LoadedMaps.Add(mapName, map);
	}

	public static void LoadMap(string mapName)
	{
		MapSchematic map = GetMapData(mapName);
		UnloadMap(mapName);
		map.Reload(GetPrioritySchematicNames());

		LoadedMaps.Add(mapName, map);
	}

	/// <summary>
	/// マップを複数フレームへ分散してロードする（1フレームの処理時間ストールを避ける）。
	/// ロード開始時点で <see cref="LoadedMaps"/> に登録され、SpawnedObjects は徐々に増える。
	/// <see cref="Configs.Config.PrioritySchematics"/> に指定したスキマティックは他より先に処理される。
	/// </summary>
	public static CoroutineHandle LoadMapStaggered(string mapName, float frameBudgetMs = DefaultLoadFrameBudgetMs)
	{
		MapSchematic map = GetMapData(mapName);
		UnloadMap(mapName);
		LoadedMaps.Add(mapName, map);

		CoroutineHandle handle = Timing.RunCoroutine(RunStaggeredLoad(mapName, map, frameBudgetMs));
		StaggeredLoads[mapName] = handle;
		return handle;
	}

	private static IEnumerator<float> RunStaggeredLoad(string mapName, MapSchematic map, float frameBudgetMs)
	{
		IEnumerator<float> reload = map.ReloadStaggered(frameBudgetMs, GetPrioritySchematicNames());
		while (true)
		{
			// ロード中にアンロード / 別ロードへ差し替えられたら中断する
			if (!LoadedMaps.TryGetValue(mapName, out MapSchematic current) || current != map)
				yield break;

			if (!reload.MoveNext())
				break;

			yield return reload.Current;
		}

		StaggeredLoads.Remove(mapName);
		Logger.Debug($"Staggered load of map {mapName} completed ({map.SpawnedObjects.Count} objects).");
	}

	private static List<string> GetPrioritySchematicNames()
		=> ProjectMER.Singleton?.Config?.PrioritySchematics ?? [];

	public static bool UnloadMap(string mapName)
	{
		if (StaggeredLoads.TryGetValue(mapName, out CoroutineHandle staggeredHandle))
		{
			Timing.KillCoroutines(staggeredHandle);
			StaggeredLoads.Remove(mapName);
		}

		if (!LoadedMaps.ContainsKey(mapName))
			return false;

		foreach (MapEditorObject mapEditorObject in LoadedMaps[mapName].SpawnedObjects)
			mapEditorObject.Destroy();

		LoadedMaps.Remove(mapName);
		return true;
	}

	public static bool TryGetMapData(string mapName, out MapSchematic mapSchematic)
	{
		try
		{
			mapSchematic = GetMapData(mapName);
			return true;
		}
		catch (Exception)
		{
			mapSchematic = null!;
			return false;
		}
	}

	public static MapSchematic GetMapData(string mapName)
	{
		MapSchematic map;

		string path = Path.Combine(ProjectMER.MapsDir, $"{mapName}.yml");
		if (!File.Exists(path))
		{
			string error = $"Failed to load map data: File {mapName}.yml does not exist!";
			throw new FileNotFoundException(error);
		}

		try
		{
			map = YamlParser.Deserializer.Deserialize<MapSchematic>(File.ReadAllText(path));
			map.Name = mapName;
		}
		catch (YamlException e)
		{
			string error = $"Failed to load map data: File {mapName}.yml has YAML errors!\n{e.ToString().Split('\n')[0]}";
			throw new YamlException(error);
		}

		return map;
	}

	public static bool TryGetSchematicDataByName(string schematicName, out SchematicObjectDataList data)
	{
		try
		{
			data = GetSchematicDataByName(schematicName);
			return true;
		}
		catch (Exception)
		{
			data = null!;
			return false;
		}
	}

	public static SchematicObjectDataList GetSchematicDataByName(string schematicName)
	{
		SchematicObjectDataList data;
		string schematicDirPath = Path.Combine(ProjectMER.SchematicsDir, schematicName);
		string schematicJsonPath = Path.Combine(schematicDirPath, $"{schematicName}.json");
		string misplacedSchematicJsonPath = schematicDirPath + ".json";

		if (!Directory.Exists(schematicDirPath))
		{
			// Some users may throw a single JSON file into Schematics folder, this automatically creates and moved the file to the correct schematic directory.
			if (File.Exists(misplacedSchematicJsonPath))
			{
				Directory.CreateDirectory(schematicDirPath);
				File.Move(misplacedSchematicJsonPath, schematicJsonPath);
				return GetSchematicDataByName(schematicName);
			}

			string error = $"Failed to load schematic data: Directory {schematicName} does not exist!";
			Logger.Error(error);
			throw new DirectoryNotFoundException(error);
		}

		if (!File.Exists(schematicJsonPath))
		{
			// Same as above but with the folder existing and file not being there for some reason.
			if (File.Exists(misplacedSchematicJsonPath))
			{
				File.Move(misplacedSchematicJsonPath, schematicJsonPath);
				return GetSchematicDataByName(schematicName);
			}

			string error = $"Failed to load schematic data: File {schematicName}.json does not exist!";
			Logger.Error(error);
			throw new FileNotFoundException(error);
		}

		try
		{
			data = JsonSerializer.Deserialize<SchematicObjectDataList>(File.ReadAllText(schematicJsonPath));
			data.Path = schematicDirPath;
		}
		catch (JsonParsingException e)
		{
			string error = $"Failed to load schematic data: File {schematicName}.json has JSON errors!\n{e.ToString().Split('\n')[0]}";
			Logger.Error(error);
			throw new JsonParsingException(error);
		}

		return data;
	}

	public static string[] GetAvailableSchematicNames() => Directory.GetFiles(ProjectMER.SchematicsDir, "*.json", SearchOption.AllDirectories).Select(Path.GetFileNameWithoutExtension).Where(x => !x.Contains('-')).ToArray();

	public static string GetColoredMapName(string mapName)
	{
		if (mapName == UntitledMapName)
			return $"<color=grey><b><i>{UntitledMapName}</i></b></color>";

		bool isDirty = false;
		if (LoadedMaps.TryGetValue(mapName, out MapSchematic mapSchematic))
			isDirty = mapSchematic.IsDirty;

		return isDirty ? $"<i>{GetColoredString(mapName)}</i>" : GetColoredString(mapName);
	}

	public static string GetColoredString(string s)
	{
		uint value = Math.Min(((uint)s.GetHashCode()) / 255, 16777215);
		string colorHex = value.ToString("X6");
		return $"<color=#{colorHex}><b>{s}</b></color>";
	}
}
