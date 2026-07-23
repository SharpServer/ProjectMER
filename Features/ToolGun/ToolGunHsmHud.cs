using System.Runtime.CompilerServices;
using HintServiceMeow.Core.Enum;
using HintServiceMeow.Core.Models.Hints;
using HintServiceMeow.Core.Utilities;
using LabApi.Features.Wrappers;

namespace ProjectMER.Features.ToolGun;

/// <summary>
/// Shows the ToolGun HUD through HintServiceMeow's own hint queue when the plugin is loaded, instead of
/// ProjectMER's raw <see cref="Player.SendHint(string, float)"/> call, since HSM's compatibility adapter
/// for that call is opt-in (<c>UseHintCompatibilityAdapter</c> defaults to false in HSM's config) and can't
/// be relied on. Falls back silently when HintServiceMeow isn't present.
/// </summary>
internal static class ToolGunHsmHud
{
	private const string HintId = "ProjectMER.ToolGunHUD";

	public static bool IsAvailable { get; } = AppDomain.CurrentDomain.GetAssemblies()
		.Any(assembly => assembly.GetName().Name == "HintServiceMeow-Exiled");

	public static bool TryShow(Player player, string content)
	{
		if (!IsAvailable)
			return false;

		try
		{
			ShowInternal(player, content);
			return true;
		}
		catch (Exception e)
		{
			Logger.Error(e);
			return false;
		}
	}

	public static bool TryHide(Player player)
	{
		if (!IsAvailable)
			return false;

		try
		{
			HideInternal(player);
			return true;
		}
		catch (Exception e)
		{
			Logger.Error(e);
			return false;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ShowInternal(Player player, string content)
	{
		PlayerDisplay display = PlayerDisplay.Get(player.ReferenceHub);
		if (display.GetHint(HintId) is Hint hint)
		{
			hint.Hide = false;
			hint.Text = content;
			return;
		}

		display.AddHint(new Hint
		{
			Id = HintId,
			Text = content,
			Alignment = HintAlignment.Center,
			SyncSpeed = HintSyncSpeed.Fast,
			FontSize = 20,
			YCoordinate = 870f,
			ResolutionBasedAlign = true,
		});
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void HideInternal(Player player)
	{
		PlayerDisplay display = PlayerDisplay.Get(player.ReferenceHub);
		if (display.GetHint(HintId) is Hint hint)
			hint.Hide = true;
	}
}
