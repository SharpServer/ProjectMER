using System.Collections;
using System.ComponentModel;
using System.Reflection;
using System.Text;
using CommandSystem;
using LabApi.Features.Permissions;
using LabApi.Features.Wrappers;
using NorthwoodLib.Pools;
using ProjectMER.Features;
using ProjectMER.Features.Extensions;
using ProjectMER.Features.Interfaces;
using ProjectMER.Features.Objects;
using ProjectMER.Features.Serializable;
using ProjectMER.Features.ToolGun;
using UnityEngine;
using Utils.NonAllocLINQ;

namespace ProjectMER.Commands.Modifying;

/// <summary>
/// Command used for modifying the objects.
/// </summary>
public class Modify : ICommand
{
	/// <inheritdoc/>
	public string Command => "modify";

	/// <inheritdoc/>
	public string[] Aliases { get; } = ["mod"];

	/// <inheritdoc/>
	public string Description => "Allows modifying properties of the selected object.";

	/// <inheritdoc/>
	public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
	{
		if (!sender.HasAnyPermission($"mpr.{Command}"))
		{
			response = $"You don't have permission to execute this command. Required permission: mpr.{Command}";
			return false;
		}

		Player? player = Player.Get(sender);
		if (player is null)
		{
			response = "This command can't be run from the server console.";
			return false;
		}

		if (!ToolGunHandler.TryGetSelectedMapObject(player, out MapEditorObject mapEditorObject))
		{
			response = "You haven't selected any object!";
			return false;
		}

		object instance = mapEditorObject.GetType().GetField("Base").GetValue(mapEditorObject);
		List<PropertyInfo> properties = instance.GetType().GetModifiableProperties().ToList();

		if (arguments.Count == 0)
		{
			List<PropertyInfo> displayProperties = instance is SerializableObjectPrefabMarker
				? properties.Where(x => x.Name != nameof(SerializableObjectPrefabMarker.Options)).ToList()
				: properties;

			StringBuilder sb = StringBuilderPool.Shared.Rent();
			sb.AppendLine();
			sb.Append("Object properties:");
			sb.AppendLine();
			sb.AppendLine();
			sb.Append($"MapName: {MapUtils.GetColoredMapName(mapEditorObject.MapName)}");
			sb.AppendLine();
			sb.Append($"ID: {MapUtils.GetColoredString(mapEditorObject.Id)}");
			sb.AppendLine();
			foreach (string property in displayProperties.GetColoredProperties(instance))
			{
				sb.Append(property);
				sb.AppendLine();
			}

			if (instance is SerializableObjectPrefabMarker prefabMarker)
			{
				sb.AppendLine();
				sb.Append(BuildPrefabOptionInfo(prefabMarker));
			}

			response = StringBuilderPool.Shared.ToStringReturn(sb);
			return true;
		}

		string propertyName = arguments.At(0).ToUpperInvariant();
		if (propertyName.Contains("MAP"))
			return HandleMap(out response);
		else if (propertyName == "ID")
			return HandleId(out response);

		PropertyInfo? foundProperty = properties.FirstOrDefault(x => x.Name.Equals(arguments.At(0), StringComparison.OrdinalIgnoreCase))
			?? properties.FirstOrDefault(x => x.Name.ToUpperInvariant().Contains(propertyName));
		if (foundProperty == null)
		{
			response = $"There isn't any object property that contains \"{arguments.At(0)}\" in it's name!";
			return false;
		}

		if (foundProperty.Name == nameof(SerializableObject.Room))
			return HandleRoom(out response);

		bool result;
		if (typeof(IDictionary).IsAssignableFrom(foundProperty.PropertyType))
			result = HandleDictionary(out response);
		else if (typeof(ICollection).IsAssignableFrom(foundProperty.PropertyType))
			result = HandleCollection(out response);
		else if (foundProperty.PropertyType != typeof(string))
			result = HandleNonString(out response);
		else result = HandleString(out response);

		if (!result)
			return false;

		mapEditorObject.UpdateObjectAndCopies();
		response = "You've successfully modified the object!";
		return true;




		bool HandleMap(out string response)
		{
			if (arguments.Count < 2)
			{
				response = "Not enough arguments!";
				return false;
			}

			string newMapName = arguments.At(1);
			if (mapEditorObject.MapName == newMapName)
			{
				response = $"This object is already a part of this map!";
				return false;
			}

			if (newMapName == MapUtils.UntitledMapName)
			{
				response = $"This map name is reserved for internal use!";
				return false;
			}

			MapSchematic oldMap = mapEditorObject.Map;
			if (!MapUtils.LoadedMaps.TryGetValue(newMapName, out MapSchematic newMap)) // Map is already loaded
				if (!MapUtils.TryGetMapData(newMapName, out newMap)) // Map isn't loaded but map file exists
				{ // Map isn't loaded and map file doesn't exist

					newMap = new MapSchematic(newMapName);
					MapUtils.LoadedMaps.Add(newMapName, newMap);
				}


			oldMap.TryRemoveElement(mapEditorObject.Id);
			newMap.TryAddElement(mapEditorObject.Id, mapEditorObject.Base);

			oldMap.Reload();
			newMap.Reload();
			response = "You've successfully modified the object's map!";
			return true;
		}

		bool HandleId(out string response)
		{
			if (arguments.Count < 2)
			{
				response = "Not enough arguments!";
				return false;
			}

			string newId = arguments.At(1);

			if (mapEditorObject.Map.SpawnedObjects.Any(x => x.Id == newId))
			{
				response = $"This ID is already used by an other object!";
				return false;
			}

			mapEditorObject.Map.TryAddElement(newId, mapEditorObject.Base);
			mapEditorObject.Map.TryRemoveElement(mapEditorObject.Id);
			mapEditorObject.Map.Reload();
			response = "You've successfully modified the object's ID!";
			return true;
		}

		bool HandleRoom(out string response)
		{
			if (arguments.Count < 2)
			{
				response = "You need to provide a room value!";
				return false;
			}

			Vector3 worldPosition = mapEditorObject.transform.position;
			Quaternion worldRotation = mapEditorObject.transform.rotation;
			string previousRoom = mapEditorObject.Base.Room;
			string newRoom = string.Join(" ", arguments.Skip(1));

			mapEditorObject.Base.Room = newRoom;
			List<Room> rooms = mapEditorObject.Base.GetRooms();
			if (rooms.Count == 0)
			{
				mapEditorObject.Base.Room = previousRoom;
				ListPool<Room>.Shared.Return(rooms);
				response = $"There are no rooms matching \"{newRoom}\" in the current map!";
				return false;
			}

			Room? indexedRoom = rooms
				.Where(room => mapEditorObject.Base.Index < 0 || room.GetRoomIndex() == mapEditorObject.Base.Index)
				.OrderBy(room => (room.Transform.position - worldPosition).sqrMagnitude)
				.FirstOrDefault();
			Room targetRoom = indexedRoom ?? rooms.OrderBy(room => (room.Transform.position - worldPosition).sqrMagnitude).First();
			if (mapEditorObject.Base.Index >= 0 && indexedRoom is null)
				mapEditorObject.Base.Index = targetRoom.GetRoomIndex();

			if (targetRoom.Name == MapGeneration.RoomName.Outside)
			{
				mapEditorObject.Base.Position = worldPosition;
				mapEditorObject.Base.Rotation = worldRotation.eulerAngles;
			}
			else
			{
				mapEditorObject.Base.Position = targetRoom.Transform.InverseTransformPoint(worldPosition);
				mapEditorObject.Base.Rotation = (Quaternion.Inverse(targetRoom.Transform.rotation) * worldRotation).eulerAngles;
			}

			ListPool<Room>.Shared.Return(rooms);

			MapSchematic map = mapEditorObject.Map;
			string id = mapEditorObject.Id;
			SerializableObject serializableObject = mapEditorObject.Base;
			map.IsDirty = true;
			map.DestroyObject(id);
			map.SpawnObject(id, serializableObject);

			MapEditorObject? replacement = map.SpawnedObjects
				.Where(obj => obj.Id == id)
				.OrderBy(obj => (obj.transform.position - worldPosition).sqrMagnitude)
				.FirstOrDefault();
			if (replacement is not null)
				ToolGunHandler.SelectObject(player, replacement);

			response = "You've successfully modified the object's room and converted its position and rotation!";
			return true;
		}

		bool HandleCollection(out string response)
		{
			if (arguments.Count < 2)
			{
				response = "Invalid arguments! Use add/remove.";
				return false;
			}

			object listInstance = foundProperty.GetValue(instance);
			Type listType = foundProperty.PropertyType.GetInterfaces().First(x => x.IsGenericType).GetGenericArguments()[0];

			switch (arguments.At(1).ToLower())
			{
				case "a":
				case "add":
					{
						for (int i = 2; i < arguments.Count; i++)
						{
							try
							{
								object value = TypeDescriptor.GetConverter(listType).ConvertFromInvariantString(arguments.At(i));
								foundProperty.PropertyType.GetMethod("Add").Invoke(listInstance, [value]);
							}
							catch (Exception)
							{
								response = $"\"{arguments.At(i)}\" is not a valid argument! The value should be a {listType} type.";
								return false;
							}
						}
						break;
					}

				case "rm":
				case "remove":
					{
						for (int i = 2; i < arguments.Count; i++)
						{
							try
							{
								object value = TypeDescriptor.GetConverter(listType).ConvertFromInvariantString(arguments.At(i));
								foundProperty.PropertyType.GetMethod("Remove").Invoke(listInstance, [value]);
							}
							catch (Exception)
							{
								response = $"\"{arguments.At(i)}\" is not a valid argument! The value should be a {listType} type.";
								return false;
							}
						}
						break;
					}

				default:
					response = "Invalid arguments! Use add/remove.";
					return false;
			}

			response = string.Empty;
			return true;
		}

		bool HandleDictionary(out string response)
		{
			if (arguments.Count < 2)
			{
				response = "Invalid arguments! Use add/set/remove.";
				return false;
			}

			IDictionary dictionary = (IDictionary)foundProperty.GetValue(instance);
			Type[] genericArguments = foundProperty.PropertyType.GetGenericArguments();
			Type keyType = genericArguments[0];
			Type valueType = genericArguments[1];

			switch (arguments.At(1).ToLower())
			{
				case "a":
				case "add":
				case "set":
					{
						if ((arguments.Count - 2) % 2 != 0)
						{
							response = "Invalid arguments! Use key/value pairs.";
							return false;
						}

						for (int i = 2; i < arguments.Count; i += 2)
						{
							try
							{
								object key = TypeDescriptor.GetConverter(keyType).ConvertFromInvariantString(arguments.At(i));
								object value = TypeDescriptor.GetConverter(valueType).ConvertFromInvariantString(arguments.At(i + 1));
								dictionary[key] = value;
							}
							catch (Exception)
							{
								response = $"\"{arguments.At(i)}\" or \"{arguments.At(i + 1)}\" is not a valid argument! The value should be a {keyType}/{valueType} pair.";
								return false;
							}
						}

						break;
					}

				case "rm":
				case "remove":
					{
						for (int i = 2; i < arguments.Count; i++)
						{
							try
							{
								object key = TypeDescriptor.GetConverter(keyType).ConvertFromInvariantString(arguments.At(i));
								dictionary.Remove(key);
							}
							catch (Exception)
							{
								response = $"\"{arguments.At(i)}\" is not a valid argument! The value should be a {keyType} type.";
								return false;
							}
						}

						break;
					}

				default:
					response = "Invalid arguments! Use add/remove.";
					return false;
			}

			response = string.Empty;
			return true;
		}

		bool HandleNonString(out string response)
		{
			if (arguments.Count < 2 && !foundProperty.PropertyType.IsEnum)
			{
				response = $"You need to provide a {foundProperty.PropertyType} value!";
				return false;
			}

			try
			{
				object value = TypeDescriptor.GetConverter(foundProperty.PropertyType).ConvertFromInvariantString(arguments.At(1));
				foundProperty.SetValue(instance, value);
			}
			catch (Exception)
			{
				StringBuilder sb = StringBuilderPool.Shared.Rent();
				if (arguments.Count > 1)
				{
					sb.Append($"\"{arguments.At(1)}\" is not a valid argument! The value should be a {foundProperty.PropertyType} type.");
				}

				if (foundProperty.PropertyType.IsEnum)
				{
					sb.AppendLine();
					sb.Append($"{foundProperty.PropertyType.ToString().Split('.').Last()} values (use either text name or number, sum numbers for multiple flags)");
					sb.AppendLine();
					foreach (object value in Enum.GetValues(foundProperty.PropertyType))
					{
						sb.Append($"- {value} = {Enum.Format(foundProperty.PropertyType, value, "d")}");
						sb.AppendLine();
					}

					sb.Remove(sb.Length - 1, 1);
				}

				response = StringBuilderPool.Shared.ToStringReturn(sb);
				return false;
			}

			response = string.Empty;
			return true;
		}

		bool HandleString(out string response)
		{
			if (arguments.Count < 2)
			{
				response = "You need to provide a string value!";
				return false;
			}

			StringBuilder spacedStringBuilder = StringBuilderPool.Shared.Rent(arguments.At(1));
			for (int i = 1; i < arguments.Count - 1; i++)
			{
				spacedStringBuilder.Append($" {arguments.At(1 + i)}");
			}

			try
			{
				foundProperty.SetValue(instance, TypeDescriptor.GetConverter(foundProperty.PropertyType).ConvertFromInvariantString(StringBuilderPool.Shared.ToStringReturn(spacedStringBuilder)));
			}
			catch (Exception)
			{
				response = $"\"{arguments.At(1)}\" is not a valid argument! The value should be a {foundProperty.PropertyType} type.";
				return false;
			}

			response = string.Empty;
			return true;
		}
	}

	private static string BuildPrefabOptionInfo(SerializableObjectPrefabMarker marker)
	{
		StringBuilder sb = StringBuilderPool.Shared.Rent();
		sb.Append("PrefabType options:");
		sb.AppendLine();

		IObjectPrefabInfoProvider? provider = ObjectPrefabInfoRegistry.Provider;
		IReadOnlyList<ObjectPrefabOptionInfo> definitions = Array.Empty<ObjectPrefabOptionInfo>();
		string error = string.Empty;
		if (provider is null || string.IsNullOrWhiteSpace(marker.PrefabType) ||
			!provider.TryGetOptionDefinitions(marker.PrefabType, out definitions, out error))
		{
			sb.Append(provider is null
				? "(No ObjectPrefab info provider registered; showing raw Options only)"
				: $"(Could not resolve options for PrefabType '{marker.PrefabType}': {error})");
			sb.AppendLine();

			foreach (KeyValuePair<string, string> option in marker.Options)
			{
				sb.Append($"- {option.Key} = {MapUtils.GetColoredString(option.Value)}");
				sb.AppendLine();
			}

			return StringBuilderPool.Shared.ToStringReturn(sb);
		}

		foreach (ObjectPrefabOptionInfo definition in definitions)
		{
			string currentValue = marker.Options.TryGetValue(definition.Name, out string raw) ? raw : definition.DefaultValue ?? string.Empty;
			sb.Append($"- {definition.Name}:{definition.ValueType}" +
				(definition.ConstraintDescription != null ? $"({definition.ConstraintDescription})" : string.Empty) +
				$" = <color=yellow><b>{currentValue}</b></color>" +
				(definition.DefaultValue != null ? $" [default: {definition.DefaultValue}]" : string.Empty));
			sb.AppendLine();
		}

		return StringBuilderPool.Shared.ToStringReturn(sb);
	}
}
