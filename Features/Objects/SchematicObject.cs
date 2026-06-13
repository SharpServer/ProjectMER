using AdminToys;
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
        set
        {
            transform.position = value;
        }
    }

    /// <summary>
    /// Gets or sets the global rotation of the object.
    /// </summary>
    public Quaternion Rotation
    {
        get => transform.rotation;
        set
        {
            transform.rotation = value;
        }
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
        set
        {
            transform.localScale = value;
        }
    }

    public IReadOnlyList<GameObject> AttachedBlocks
    {
        get
        {
            if (_attachedBlocks.Count != 0 && _attachedBlocks.All(x => x != null))
                return _attachedBlocks;

            _attachedBlocks.Clear();
            foreach (Transform transform in GetComponentsInChildren<Transform>())
            {
                if (transform == this.transform)
                    continue;

                _attachedBlocks.Add(transform.gameObject);
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

    public SchematicObject Init(SchematicObjectDataList data)
    {
        Name = Path.GetFileNameWithoutExtension(data.Path);
        DirectoryPath = data.Path;

        ObjectFromId = new Dictionary<int, Transform>(data.Blocks.Count + 1)
        {
            { data.RootObjectId, transform },
        };

        CreateRecursiveFromID(data.RootObjectId, data.Blocks, transform);

        AddRigidbodies();
        AddTeleports();
        AddTriggerPoints();
        AddObjectPrefabs();
        AddDoors();
        AddAnimators();

        Schematic.OnSchematicSpawned(new(this, Name));

        return this;
    }

    private void CreateRecursiveFromID(int id, List<SchematicBlockData> blocks, Transform parentGameObject)
    {
        Transform childGameObjectTransform = CreateObject(blocks.Find(c => c.ObjectId == id), parentGameObject) ?? transform;
        int[] parentSchematics = blocks.Where(bl => bl.BlockType == BlockType.Schematic).Select(bl => bl.ObjectId).ToArray();

        foreach (SchematicBlockData block in blocks.FindAll(c => c.ParentId == id))
        {
            if (parentSchematics.Contains(block.ParentId))
                continue;

            CreateRecursiveFromID(block.ObjectId, blocks, childGameObjectTransform);
        }
    }

    private Transform? CreateObject(SchematicBlockData block, Transform parentTransform)
    {
        if (block == null)
            return null;

        GameObject gameObject = block.Create(this, parentTransform);
        NetworkServer.Spawn(gameObject);

        ObjectFromId.Add(block.ObjectId, gameObject.transform);

        if (block.BlockType != BlockType.Light && TryGetAnimatorController(block.AnimatorName, out RuntimeAnimatorController animatorController))
            _animators.Add(gameObject, animatorController);

        return gameObject.transform;
    }

    private bool TryGetAnimatorController(string animatorName, out RuntimeAnimatorController animatorController)
    {
        animatorController = null!;

        if (string.IsNullOrEmpty(animatorName))
            return false;

        Object? animatorObject = AssetBundle.GetAllLoadedAssetBundles().FirstOrDefault(x => x.mainAsset.name == animatorName)?.LoadAllAssets().First(x => x is RuntimeAnimatorController);

        if (animatorObject is null)
        {
            string path = Path.Combine(DirectoryPath, animatorName);

            if (!File.Exists(path))
            {
                Logger.Warn($"{gameObject.name} block of schematic should have a {animatorName} animator attached, but the file does not exist!");
                return false;
            }

            animatorObject = AssetBundle.LoadFromFile(path).LoadAllAssets().First(x => x is RuntimeAnimatorController);
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

        List<SchematicTeleportData> teleportDataList = JsonSerializer.Deserialize<List<SchematicTeleportData>>(File.ReadAllText(teleportPath));

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
            boxCollider.isTrigger = true;
            boxCollider.size = teleportData.TriggerScale;

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

        List<SchematicTriggerPointData> triggerPointDataList = JsonSerializer.Deserialize<List<SchematicTriggerPointData>>(File.ReadAllText(triggerPointPath));

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

        List<SchematicObjectPrefabData> objectPrefabDataList = JsonSerializer.Deserialize<List<SchematicObjectPrefabData>>(File.ReadAllText(objectPrefabPath));

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

        List<SchematicDoorData> doorDataList = YamlParser.Deserializer.Deserialize<List<SchematicDoorData>>(File.ReadAllText(doorPath));

        foreach (SchematicDoorData doorData in doorDataList)
        {
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

        foreach (KeyValuePair<int, SerializableRigidbody> dict in JsonSerializer.Deserialize<Dictionary<int, SerializableRigidbody>>(File.ReadAllText(rigidbodyPath)))
        {
            if (!ObjectFromId.TryGetValue(dict.Key, out Transform transform))
                continue;

            if (!transform.gameObject.TryGetComponent(out Rigidbody rigidbody))
                rigidbody = transform.gameObject.AddComponent<Rigidbody>();

            rigidbody.isKinematic = dict.Value.IsKinematic;
            rigidbody.useGravity = dict.Value.UseGravity;
            rigidbody.constraints = dict.Value.Constraints;
            rigidbody.mass = dict.Value.Mass;

            hasRigidbodies = true;
        }

        return hasRigidbodies;
    }


    public void Destroy() => Destroy(gameObject);

    private void OnDestroy()
    {
        AnimationController.Dictionary.Remove(this);
        NetworkServer.Destroy(gameObject);
        Schematic.OnSchematicDestroyed(new(this, Name));
    }

    internal Dictionary<int, Transform> ObjectFromId = [];

    private readonly List<GameObject> _attachedBlocks = [];
    private readonly List<NetworkIdentity> _networkIdentities = [];
    private readonly List<AdminToyBase> _adminToyBases = [];
    private readonly Dictionary<GameObject, RuntimeAnimatorController> _animators = [];
}
