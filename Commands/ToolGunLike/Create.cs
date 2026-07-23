using System.Text;
using CommandSystem;
using LabApi.Features.Permissions;
using LabApi.Features.Wrappers;
using NorthwoodLib.Pools;
using ProjectMER.Configs;
using ProjectMER.Features;
using ProjectMER.Features.Enums;
using ProjectMER.Features.Interfaces;
using ProjectMER.Features.Serializable;
using ProjectMER.Features.ToolGun;
using UnityEngine;
using static ProjectMER.Features.Extensions.StructExtensions;

namespace ProjectMER.Commands.ToolGunLike;

public class Create : ICommand
{
	/// <inheritdoc/>
	public string Command => "create";

	/// <inheritdoc/>
	public string[] Aliases { get; } = ["cr", "spawn"];

	/// <inheritdoc/>
	public string Description => "Creates a selected object at the point you are looking at.";

	/// <inheritdoc/>
	public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
	{
		if (!sender.HasAnyPermission($"mpr.{Command}"))
		{
			response = $"You don't have permission to execute this command. Required permission: mpr.{Command}";
			return false;
		}

		Player? player = Player.Get(sender)!;

		if (arguments.Count == 0)
		{
			StringBuilder sb = StringBuilderPool.Shared.Rent();
			sb.AppendLine();
			sb.Append("List of all spawnable objects:");
			sb.AppendLine();
			sb.AppendLine();
			foreach (ToolGunObjectType objectType in ToolGunItem.TypesDictionary.Keys)
			{
				if (objectType == ToolGunObjectType.Schematic)
					continue;

				sb.Append($"- {objectType} ({(int)objectType})");
				sb.AppendLine();
			}

			sb.AppendLine();
			sb.Append("To spawn a custom schematic, please use it's file name as an argument.");

			response = StringBuilderPool.Shared.ToStringReturn(sb);
			return true;
		}

		Vector3 position = Vector3.zero;
		if (arguments.Count >= 4 && !TryGetVector(arguments.At(1), arguments.At(2), arguments.At(3), out position))
		{
			response = "Invalid arguments. Usage: mp create <object> <posX> <posY> <posZ>";
			return false;
		}

		if (arguments.Count == 1)
		{
			if (!ToolGunHandler.Raycast(player, out RaycastHit hit))
			{
				response = "Couldn't find a valid surface on which the object could be spawned!";
				return false;
			}

			position = hit.point;
		}
		else if (arguments.Count < 4)
		{
			response = "Invalid arguments. Usage: mp create <object> optionally: <posX> <posY> <posZ>";
			return false;
		}

		string objectName = arguments.At(0);

		if (objectName.StartsWith("o:", StringComparison.OrdinalIgnoreCase))
			return HandlePrefabShortcut(objectName.Substring(2), position, player, out response);

		if (Enum.TryParse(objectName, true, out ToolGunObjectType parsedEnum) && Enum.IsDefined(typeof(ToolGunObjectType), parsedEnum))
		{
			var createdObject = ToolGunHandler.CreateObjectAndGet(position, parsedEnum);
			if (createdObject is null)
			{
				response = $"{objectName} could not be spawned!";
				return false;
			}

			if (Config.AutoSelect && player is not null)
				ToolGunHandler.SelectObject(player, createdObject);

			response = $"{objectName} has been successfully spawned!";
			return true;
		}

		try
		{
			_ = MapUtils.GetSchematicDataByName(objectName);
		}
		catch (Exception e)
		{
			response = e.Message.ToString();
			return false;
		}

		var schematicObject = ToolGunHandler.CreateObjectAndGet(position, ToolGunObjectType.Schematic, objectName);
		if (schematicObject is null)
		{
			response = $"{objectName} could not be spawned!";
			return false;
		}

		if (Config.AutoSelect && player is not null)
			ToolGunHandler.SelectObject(player, schematicObject);

		response = $"{objectName} has been successfully spawned!";
		return true;
	}

	private static bool HandlePrefabShortcut(string remainder, Vector3 position, Player? player, out string response)
	{
		int colonIndex = remainder.IndexOf(':');
		string prefabTypeName = colonIndex >= 0 ? remainder.Substring(0, colonIndex) : remainder;
		string optionsCsv = colonIndex >= 0 ? remainder.Substring(colonIndex + 1) : string.Empty;

		if (string.IsNullOrWhiteSpace(prefabTypeName))
		{
			response = BuildAvailablePrefabTypesMessage();
			return true;
		}

		Dictionary<string, string> options = new();
		if (!string.IsNullOrEmpty(optionsCsv))
		{
			foreach (string pair in optionsCsv.Split(','))
			{
				if (string.IsNullOrWhiteSpace(pair))
					continue;

				int equalsIndex = pair.IndexOf('=');
				if (equalsIndex <= 0)
				{
					response = $"Invalid option \"{pair}\". Expected Key=Value.";
					return false;
				}

				options[pair.Substring(0, equalsIndex).Trim()] = pair.Substring(equalsIndex + 1).Trim();
			}
		}

		var createdObject = ToolGunHandler.CreateObjectAndGet(position, ToolGunObjectType.ObjectPrefabMarker);
		if (createdObject is null)
		{
			response = $"{prefabTypeName} could not be spawned!";
			return false;
		}

		var marker = (SerializableObjectPrefabMarker)createdObject.Base;
		marker.PrefabType = prefabTypeName;
		foreach (KeyValuePair<string, string> option in options)
			marker.Options[option.Key] = option.Value;

		createdObject.UpdateObjectAndCopies();

		if (Config.AutoSelect && player is not null)
			ToolGunHandler.SelectObject(player, createdObject);

		response = $"{prefabTypeName} (ObjectPrefabMarker) has been successfully spawned!";
		return true;
	}

	private static string BuildAvailablePrefabTypesMessage()
	{
		IObjectPrefabInfoProvider? provider = ObjectPrefabInfoRegistry.Provider;
		if (provider is null)
			return "No ObjectPrefab info provider is registered (the plugin supplying PrefabTypes isn't loaded). Enter the PrefabType name manually, e.g. o:ControllableLight";

		IReadOnlyList<ObjectPrefabTypeInfo> types = provider.GetPrefabTypes();
		if (types.Count == 0)
			return "No ObjectPrefab types are registered.";

		StringBuilder sb = StringBuilderPool.Shared.Rent();
		sb.AppendLine();
		sb.Append("Available ObjectPrefab types:");
		sb.AppendLine();
		sb.AppendLine();
		foreach (ObjectPrefabTypeInfo type in types)
		{
			sb.Append($"- {type.Key}");
			if (type.Aliases.Count > 0)
				sb.Append($" (aliases: {string.Join(", ", type.Aliases)})");
			sb.AppendLine();
		}

		return StringBuilderPool.Shared.ToStringReturn(sb);
	}

	private static Config Config => ProjectMER.Singleton.Config!;
}
