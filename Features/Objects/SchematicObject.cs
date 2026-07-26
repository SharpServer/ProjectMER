using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using AdminToys;
using MEC;
using Mirror;
using ProjectMER.Events.Handlers;
using ProjectMER.Features;
using ProjectMER.Features.Enums;
using ProjectMER.Features.Serializable;
using ProjectMER.Features.Serializable.Schematics;
using UnityEngine;
using Utf8Json;
using Utils.NonAllocLINQ;
using Object = UnityEngine.Object;

namespace ProjectMER.Features.Objects;

public class SchematicObject : MonoBehaviour
{
    /// <summary>
    /// Gets the schematic name.
    /// </summary>
    public string Name { get; private set; }

    /// <summary>
    /// Gets a schematic directory path.
    /// </summary>
    public string DirectoryPath { get; private set; }

    /// <summary>
    /// Gets or sets the global position of the object.
    /// </summary>
    public Vector3 Position
    {
        get => transform.position;
        set => transform.position = value;
    }

    /// <summary>
    /// Gets or sets the global rotation of the object.
    /// </summary>
    public Quaternion Rotation
    {
        get => transform.rotation;
        set => transform.rotation = value;
    }

    /// <summary>
    /// Gets or sets the global euler angles of the object.
    /// </summary>
    public Vector3 EulerAngles
    {
        get => Rotation.eulerAngles;
        set => Rotation = Quaternion.Euler(value);
    }

    /// <summary>
    /// Gets or sets the scale of the object.
    /// </summary>
    public Vector3 Scale
    {
        get => transform.localScale;
        set => transform.localScale = value;
    }

    public IReadOnlyList<GameObject> AttachedBlocks
    {
        get
        {
            if (_attachedBlocks.Count != 0 && _attachedBlocks.All(x => x != null))
                return _attachedBlocks;

            _attachedBlocks.Clear();
            foreach (Transform tf in GetComponentsInChildren<Transform>())
            {
                if (tf == transform)
                    continue;

                _attachedBlocks.Add(tf.gameObject);
            }

            return _attachedBlocks;
        }
    }

    public IReadOnlyList<NetworkIdentity> NetworkIdentities
    {
        get
        {
            if (_networkIdentities.Count > 0 && _networkIdentities.All(x => x != null))
                return _networkIdentities;

            _networkIdentities.Clear();
            foreach (GameObject block in AttachedBlocks)
            {
                if (block.TryGetComponent(out NetworkIdentity networkIdentity))
                    _networkIdentities.Add(networkIdentity);
            }

            return _networkIdentities;
        }
    }

    public IReadOnlyList<AdminToyBase> AdminToyBases
    {
        get
        {
            if (_adminToyBases.Count > 0 && _adminToyBases.All(x => x != null))
                return _adminToyBases;

            _adminToyBases.Clear();
            foreach (NetworkIdentity netId in NetworkIdentities)
            {
                if (netId.TryGetComponent(out AdminToyBase adminToyBase))
                    _adminToyBases.Add(adminToyBase);
            }

            return _adminToyBases;
        }
    }

    public AnimationController AnimationController => AnimationController.Get(this);

    /// <summary>
    /// このスキマティックを構成する全ブロック（SchematicBlockData 由来）。
    /// </summary>
    public IReadOnlyList<SchematicBlock> Blocks
    {
        get
        {
            _blocks.RemoveAll(block => block == null);
            return _blocks;
        }
    }

    /// <summary>
    /// 名前でブロックを 1 つ検索する。完全一致（大文字小文字無視）を優先し、
    /// <paramref name="allowPartial"/> が true なら部分一致にもフォールバックする。
    /// </summary>
    public SchematicBlock? FindBlock(string name, bool allowPartial = true)
    {
        SchematicBlock? exact = null;
        SchematicBlock? partial = null;

        foreach (SchematicBlock block in Blocks)
        {
            if (string.Equals(block.BlockName, name, StringComparison.OrdinalIgnoreCase))
            {
                exact = block;
                break;
            }

            if (allowPartial && partial == null &&
                block.BlockName?.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                partial = block;
            }
        }

        return exact ?? partial;
    }

    /// <summary>
    /// 名前でブロックを列挙する。<paramref name="allowPartial"/> が true なら部分一致も含む。
    /// </summary>
    public IEnumerable<SchematicBlock> FindBlocks(string name, bool allowPartial = true)
        => Blocks.Where(block =>
            allowPartial
                ? block.BlockName?.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0
                : string.Equals(block.BlockName, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// ブロック種別（Primitive / Light / Pickup / Interactable など）で列挙する。
    /// </summary>
    public IEnumerable<SchematicBlock> FindBlocks(BlockType blockType)
        => Blocks.Where(block => block.BlockType == blockType);

    /// <summary>
    /// 条件式でブロックを列挙する。
    /// </summary>
    public IEnumerable<SchematicBlock> FindBlocks(Func<SchematicBlock, bool> predicate)
        => Blocks.Where(predicate);

    /// <summary>
    /// 指定コンポーネントを持つブロックからコンポーネントを列挙する。
    /// <paramref name="blockName"/> を指定するとブロック名（部分一致）でも絞り込む。
    /// </summary>
    public IEnumerable<T> FindBlockComponents<T>(string? blockName = null) where T : Component
    {
        foreach (SchematicBlock block in Blocks)
        {
            if (blockName != null &&
                block.BlockName?.IndexOf(blockName, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            if (block.TryGetComponent(out T component))
                yield return component;
        }
    }

    /// <summary>
    /// ObjectPrefabSchematicInfo のキーでブロックを取得する（大文字小文字無視）。
    /// </summary>
    public SchematicBlock? FindBlockByKey(string key)
        => Blocks.FirstOrDefault(block =>
            string.Equals(block.ObjectPrefabKey, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// ObjectPrefabSchematicInfo のキーを持つ（= ObjectPrefab 管理対象の）ブロックを列挙する。
    /// </summary>
    public IEnumerable<SchematicBlock> GetPrefabManagedBlocks()
        => Blocks.Where(block => block.HasObjectPrefabKey);

    /// <summary>
    /// スキマティック内オブジェクト ID からブロックを取得する。
    /// </summary>
    public SchematicBlock? GetBlockById(int objectId)
        => Blocks.FirstOrDefault(block => block.ObjectId == objectId);

    /// <summary>
    /// スキマティック内オブジェクト ID から Transform を取得する
    /// （ブロック以外のマーカー類も対象）。
    /// </summary>
    public Transform? GetTransformById(int objectId)
        => ObjectFromId.TryGetValue(objectId, out Transform tf) ? tf : null;

    // Destroy 冪等化用
    private bool _isCleanedUp;

    public SchematicObject Init(SchematicObjectDataList data)
    {
        Name = Path.GetFileNameWithoutExtension(data.Path);
        DirectoryPath = data.Path;

        ObjectFromId = new Dictionary<int, Transform>(data.Blocks.Count + 1)
        {
            { data.RootObjectId, transform },
        };

        var parentBlockIds = new HashSet<int>(data.Blocks.Select(block => block.ParentId));
        CreateRecursiveFromID(data.RootObjectId, data.Blocks, transform, parentBlockIds);

        AddRigidbodies();
        AddTeleports();
		AddTriggerPoints();
		AddEventInvokeMarkers();
		AddObjectPrefabs();
        AddDoors();
        AddAnimators();

        Schematic.OnSchematicSpawned(new(this, Name));

        return this;
    }

    /// <summary>
    /// Init の分散実行版。ブロック生成を複数フレームへ分散し、数千～数万ブロック規模の
    /// スキマティック（例: PersonnelOfficeZone は34080ブロック）が単一フレームで
    /// メインスレッドを長時間占有するのを避ける。
    /// </summary>
    public IEnumerator<float> InitStaggered(SchematicObjectDataList data, float frameBudgetMs, Action<SchematicObject>? onComplete = null)
    {
        Name = Path.GetFileNameWithoutExtension(data.Path);
        DirectoryPath = data.Path;

        ObjectFromId = new Dictionary<int, Transform>(data.Blocks.Count + 1)
        {
            { data.RootObjectId, transform },
        };

        // GameObject を作らず、親→子の生成順序だけを先に決定する（O(n)）。
        // 元の CreateRecursiveFromID は List.Find/FindAll を各ノードで呼ぶ O(n^2) 構造だったため、
        // ここで辞書ベースの走査に置き換え、大規模スキマティックでのアルゴリズム的な遅さも解消する。
        List<SchematicBlockData> creationOrder = BuildCreationOrder(data.RootObjectId, data.Blocks);
        var parentBlockIds = new HashSet<int>(data.Blocks.Select(block => block.ParentId));

        Stopwatch stopwatch = Stopwatch.StartNew();

        foreach (SchematicBlockData block in creationOrder)
        {
            Transform parentTransform = ObjectFromId.TryGetValue(block.ParentId, out Transform parentTf) ? parentTf : transform;
            CreateObject(block, parentTransform, !parentBlockIds.Contains(block.ObjectId));

            if (stopwatch.Elapsed.TotalMilliseconds >= frameBudgetMs)
            {
                yield return Timing.WaitForOneFrame;
                stopwatch.Restart();
            }
        }

        AddRigidbodies();
        AddTeleports();
        AddTriggerPoints();
        AddEventInvokeMarkers();
        AddObjectPrefabs();
        AddDoors();
        AddAnimators();

        Schematic.OnSchematicSpawned(new(this, Name));

        onComplete?.Invoke(this);
    }

    private void CreateRecursiveFromID(
        int id,
        List<SchematicBlockData> blocks,
        Transform parentGameObject,
        HashSet<int> parentBlockIds)
    {
        SchematicBlockData self = blocks.Find(c => c.ObjectId == id);
        Transform childGameObjectTransform = CreateObject(
            self,
            parentGameObject,
            self != null && !parentBlockIds.Contains(self.ObjectId)) ?? transform;
        int[] parentSchematics = blocks.Where(bl => bl.BlockType == BlockType.Schematic).Select(bl => bl.ObjectId).ToArray();

        foreach (SchematicBlockData block in blocks.FindAll(c => c.ParentId == id))
        {
            if (parentSchematics.Contains(block.ParentId))
                continue;

            CreateRecursiveFromID(block.ObjectId, blocks, childGameObjectTransform, parentBlockIds);
        }
    }

    /// <summary>
    /// <see cref="CreateRecursiveFromID"/> と同じ親→子の訪問順序を、GameObject を作らずに
    /// 辞書ベースの走査（O(n)）で求める。
    /// </summary>
    private static List<SchematicBlockData> BuildCreationOrder(int rootId, List<SchematicBlockData> blocks)
    {
        var byId = new Dictionary<int, SchematicBlockData>(blocks.Count);
        var childrenByParent = new Dictionary<int, List<SchematicBlockData>>(blocks.Count);

        foreach (SchematicBlockData block in blocks)
        {
            // List.Find は重複 ObjectId のうち最初の要素を返すため、辞書側も先勝ちにして挙動を一致させる。
            if (!byId.ContainsKey(block.ObjectId))
                byId[block.ObjectId] = block;

            if (!childrenByParent.TryGetValue(block.ParentId, out List<SchematicBlockData> siblings))
            {
                siblings = new List<SchematicBlockData>();
                childrenByParent[block.ParentId] = siblings;
            }

            siblings.Add(block);
        }

        var parentSchematics = new HashSet<int>(
            blocks.Where(bl => bl.BlockType == BlockType.Schematic).Select(bl => bl.ObjectId));

        var result = new List<SchematicBlockData>(blocks.Count);
        var pending = new Stack<int>();
        pending.Push(rootId);

        while (pending.Count > 0)
        {
            int id = pending.Pop();

            if (byId.TryGetValue(id, out SchematicBlockData self))
                result.Add(self);

            if (!childrenByParent.TryGetValue(id, out List<SchematicBlockData> children))
                continue;

            // Stack は LIFO なので、元の再帰と同じ左から右の訪問順序を保つには逆順に積む。
            for (int i = children.Count - 1; i >= 0; i--)
            {
                SchematicBlockData child = children[i];
                if (parentSchematics.Contains(child.ParentId))
                    continue;

                pending.Push(child.ObjectId);
            }
        }

        return result;
    }

    private Transform? CreateObject(SchematicBlockData block, Transform parentTransform, bool isLeaf)
    {
        if (block == null)
            return null;

        GameObject gameObject = block.Create(this, parentTransform, isLeaf);

        // NetworkIdentity を持たない GO（確率外れ Pickup / 遅延スポーナー等）は Spawn しない
		if (gameObject.TryGetComponent(out NetworkIdentity identity))
		{
			if (block.BlockType == BlockType.Text)
				CullingManager.PrepareSchematicText(this, block, identity);

			// Pickup.Create 等で既に Spawn 済み (netId != 0) のものを再 Spawn すると
			// "already spawned" 警告が出るだけなのでスキップする
			if (identity.netId == 0)
				NetworkServer.Spawn(gameObject);
		}

        SchematicBlock schematicBlock = gameObject.AddComponent<SchematicBlock>();
        schematicBlock.Init(this, block);
        _blocks.Add(schematicBlock);

        // ObjectId が重複したデータでもマップロード全体を巻き込んで失敗させない
        // (RootObjectId と衝突するケースも含む。最初に登録された Transform を優先する)
        if (!ObjectFromId.ContainsKey(block.ObjectId))
            ObjectFromId.Add(block.ObjectId, gameObject.transform);
        else
            Logger.Warn($"Schematic \"{Name}\" contains a duplicate ObjectId {block.ObjectId} (block \"{block.Name}\"). Keeping the first registered block.");

        if (block.BlockType != BlockType.Light && TryGetAnimatorController(block.AnimatorName, out RuntimeAnimatorController animatorController))
            _animators.Add(gameObject, animatorController);

        return gameObject.transform;
    }

    private bool TryGetAnimatorController(string animatorName, out RuntimeAnimatorController animatorController)
    {
        animatorController = null!;

        if (string.IsNullOrEmpty(animatorName))
            return false;

        Object? animatorObject = AssetBundle.GetAllLoadedAssetBundles()
            .FirstOrDefault(x => x.mainAsset.name == animatorName)?
            .LoadAllAssets()
            .First(x => x is RuntimeAnimatorController);

        if (animatorObject is null)
        {
            string path = Path.Combine(DirectoryPath, animatorName);

            if (!File.Exists(path))
            {
                Logger.Warn($"{gameObject.name} block of schematic should have a {animatorName} animator attached, but the file does not exist!");
                return false;
            }

            animatorObject = AssetBundle.LoadFromFile(path)
                .LoadAllAssets()
                .First(x => x is RuntimeAnimatorController);
        }

        animatorController = (RuntimeAnimatorController)animatorObject;
        return true;
    }

    private bool AddAnimators()
    {
        bool isAnimated = false;
        if (!_animators.IsEmpty())
        {
            isAnimated = true;
            foreach (KeyValuePair<GameObject, RuntimeAnimatorController> pair in _animators)
                pair.Key.AddComponent<Animator>().runtimeAnimatorController = pair.Value;
        }

        _animators.Clear();
        AssetBundle.UnloadAllAssetBundles(false);
        return isAnimated;
    }

    private void AddTeleports()
    {
        string teleportPath = Path.Combine(DirectoryPath, $"{Name}-Teleports.json");
        if (!File.Exists(teleportPath))
            return;

        List<SchematicTeleportData> teleportDataList =
            JsonSerializer.Deserialize<List<SchematicTeleportData>>(File.ReadAllText(teleportPath));

        foreach (SchematicTeleportData teleportData in teleportDataList)
        {
            GameObject teleportGo = new GameObject(teleportData.Name);

            Transform parentTransform = ObjectFromId.TryGetValue(teleportData.ParentId, out Transform parentTf)
                ? parentTf
                : transform;

            teleportGo.transform.SetParent(parentTransform);
            teleportGo.transform.SetLocalPositionAndRotation(teleportData.Position, Quaternion.Euler(teleportData.Rotation));
            teleportGo.transform.localScale = teleportData.Scale;

            BoxCollider boxCollider = teleportGo.AddComponent<BoxCollider>();
            TeleportHelper.ConfigureTrigger(boxCollider, teleportData.TriggerScale);

            SchematicTeleportObject teleportComponent = teleportGo.AddComponent<SchematicTeleportObject>();
            teleportComponent.Id = teleportData.Id;
            teleportComponent.Targets = teleportData.Targets;
            teleportComponent.Cooldown = teleportData.Cooldown;

            ObjectFromId[teleportData.ObjectId] = teleportGo.transform;
        }
    }

    private void AddTriggerPoints()
    {
        string triggerPointPath = Path.Combine(DirectoryPath, $"{Name}-TriggerPoints.json");
        if (!File.Exists(triggerPointPath))
            return;

        List<SchematicTriggerPointData> triggerPointDataList =
            JsonSerializer.Deserialize<List<SchematicTriggerPointData>>(File.ReadAllText(triggerPointPath));

        foreach (SchematicTriggerPointData triggerPointData in triggerPointDataList)
        {
            GameObject triggerPointGo = new GameObject(triggerPointData.Name);

            Transform parentTransform = ObjectFromId.TryGetValue(triggerPointData.ParentId, out Transform parentTf)
                ? parentTf
                : transform;

            triggerPointGo.transform.SetParent(parentTransform);
            triggerPointGo.transform.SetLocalPositionAndRotation(triggerPointData.Position, Quaternion.Euler(triggerPointData.Rotation));
            triggerPointGo.transform.localScale = triggerPointData.Scale;

            SchematicTriggerPointObject triggerPointComponent = triggerPointGo.AddComponent<SchematicTriggerPointObject>();
            triggerPointComponent.Id = triggerPointData.Id;
            triggerPointComponent.Tag = triggerPointData.Tag;
            triggerPointComponent.Schematic = this;

            ObjectFromId[triggerPointData.ObjectId] = triggerPointGo.transform;
        }
    }

    private void AddObjectPrefabs()
    {
        string objectPrefabPath = Path.Combine(DirectoryPath, $"{Name}-ObjectPrefabs.json");
        if (!File.Exists(objectPrefabPath))
            return;

        List<SchematicObjectPrefabData> objectPrefabDataList =
            JsonSerializer.Deserialize<List<SchematicObjectPrefabData>>(File.ReadAllText(objectPrefabPath));

        foreach (SchematicObjectPrefabData objectPrefabData in objectPrefabDataList)
        {
            GameObject objectPrefabGo = new GameObject(objectPrefabData.Name);

            Transform parentTransform = ObjectFromId.TryGetValue(objectPrefabData.ParentId, out Transform parentTf)
                ? parentTf
                : transform;

            objectPrefabGo.transform.SetParent(parentTransform);
            objectPrefabGo.transform.SetLocalPositionAndRotation(objectPrefabData.Position, Quaternion.Euler(objectPrefabData.Rotation));
            objectPrefabGo.transform.localScale = objectPrefabData.Scale;

            SchematicObjectPrefabObject objectPrefabComponent = objectPrefabGo.AddComponent<SchematicObjectPrefabObject>();
            objectPrefabComponent.Init(objectPrefabData, this);

            ObjectFromId[objectPrefabData.ObjectId] = objectPrefabGo.transform;
        }
    }

    private void AddDoors()
    {
        string doorPath = Path.Combine(DirectoryPath, $"{Name}-Doors.json");
        if (!File.Exists(doorPath))
            return;

        List<SchematicDoorJsonData> doorDataList =
            JsonSerializer.Deserialize<List<SchematicDoorJsonData>>(File.ReadAllText(doorPath));

        foreach (SchematicDoorJsonData jsonData in doorDataList)
        {
            SchematicDoorData doorData = jsonData.ToDoorData();
            GameObject doorMarkerGo = new GameObject(doorData.Name);

            Transform parentTransform = ObjectFromId.TryGetValue(doorData.ParentId, out Transform parentTf)
                ? parentTf
                : transform;

            doorMarkerGo.transform.SetParent(parentTransform);
            doorMarkerGo.transform.SetLocalPositionAndRotation(doorData.Position, Quaternion.Euler(doorData.Rotation));
            doorMarkerGo.transform.localScale = doorData.Scale;

            SchematicDoorObject doorComponent = doorMarkerGo.AddComponent<SchematicDoorObject>();
            doorComponent.Init(doorData, this);

            ObjectFromId[doorData.ObjectId] = doorMarkerGo.transform;
        }
    }

	private bool AddRigidbodies()
    {
        bool hasRigidbodies = false;
        string rigidbodyPath = Path.Combine(DirectoryPath, $"{Name}-Rigidbodies.json");
        if (!File.Exists(rigidbodyPath))
            return false;

        foreach (KeyValuePair<int, SerializableRigidbody> dict in
                 JsonSerializer.Deserialize<Dictionary<int, SerializableRigidbody>>(File.ReadAllText(rigidbodyPath)))
        {
            if (!ObjectFromId.TryGetValue(dict.Key, out Transform tf))
                continue;

            if (!tf.gameObject.TryGetComponent(out Rigidbody rigidbody))
                rigidbody = tf.gameObject.AddComponent<Rigidbody>();

            rigidbody.isKinematic = dict.Value.IsKinematic;
            rigidbody.useGravity = dict.Value.UseGravity;
            rigidbody.constraints = dict.Value.Constraints;
            rigidbody.mass = dict.Value.Mass;

            hasRigidbodies = true;
        }

		return hasRigidbodies;
	}

	private void AddEventInvokeMarkers()
	{
		string markerPath = Path.Combine(DirectoryPath, $"{Name}-EventInvokeMarkers.json");
		if (!File.Exists(markerPath))
			return;

		List<SchematicEventInvokeMarkerData> markers = JsonSerializer.Deserialize<List<SchematicEventInvokeMarkerData>>(File.ReadAllText(markerPath));
		foreach (SchematicEventInvokeMarkerData markerData in markers)
		{
			GameObject markerGo = new GameObject(markerData.Name);
			Transform parentTransform = ObjectFromId.TryGetValue(markerData.ParentId, out Transform parentTf) ? parentTf : transform;
			markerGo.transform.SetParent(parentTransform);
			markerGo.transform.SetLocalPositionAndRotation(markerData.Position, Quaternion.Euler(markerData.Rotation));
			markerGo.transform.localScale = markerData.Scale;

			EventInvokeMarkerObject marker = markerGo.AddComponent<EventInvokeMarkerObject>();
			marker.Id = markerData.Id;
			marker.Tag = markerData.Tag;
			marker.Distance = Mathf.Max(0.01f, markerData.Distance);
			marker.Schematic = this;
			ObjectFromId[markerData.ObjectId] = markerGo.transform;
		}
	}

	// 外部から呼ばれる Destroy。冪等。
	public void Destroy()
    {
        DestroyInternal();
    }

    private void DestroyInternal()
    {
        if (_isCleanedUp)
            return;

		_isCleanedUp = true;
		CullingManager.UnregisterSchematic(this);

        // AnimationController の辞書から削除
        AnimationController.Dictionary.Remove(this);

        // ネットワークオブジェクト破棄
        if (NetworkServer.active && gameObject != null)
        {
            var netId = gameObject.GetComponent<NetworkIdentity>();
            if (netId != null && netId.isServer)
            {
                NetworkServer.Destroy(gameObject);
            }
        }

        // イベント発火
        Schematic.OnSchematicDestroyed(new(this, Name));

        // GameObject 自体の Destroy 予約
        if (this != null && gameObject != null)
            Object.Destroy(gameObject);
    }

    private void OnDestroy()
    {
        // Unity が直接 Destroy したときにも Cleanup するが、二重実行は避ける
        if (!_isCleanedUp)
            DestroyInternal();
    }

    internal Dictionary<int, Transform> ObjectFromId = [];

    private readonly List<SchematicBlock> _blocks = [];
    private readonly List<GameObject> _attachedBlocks = [];
    private readonly List<NetworkIdentity> _networkIdentities = [];
    private readonly List<AdminToyBase> _adminToyBases = [];
    private readonly Dictionary<GameObject, RuntimeAnimatorController> _animators = [];
}
