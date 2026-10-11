using Ironmon.Tracker.App.Controls;
using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Settings;

/// <summary>
/// Renders one keyboard or controller control using optically balanced vector symbols.
/// </summary>
public partial class ControlBindingToken
{
    private const string _directionCapClass = "tracker-control-direction-cap";
    private const string _menuLabelClass = "tracker-control-menu-label";
    private const string _upPath = "M12 19V5M5 12l7-7 7 7";
    private const string _downPath = "M12 5v14M5 12l7 7 7-7";
    private const string _leftPath = "M19 12H5M12 5l-7 7 7 7";
    private const string _rightPath = "M5 12h14M12 5l7 7-7 7";
    private const string _crossPath = "M6 6l12 12M18 6L6 18";
    private const string _circlePath = "M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0";
    private const string _squarePath = "M3 3h18v18H3z";
    private const string _trianglePath = "M12 2L23 21H1z";
    private const string _crossClass = "tracker-control-cross";
    private const string _circleClass = "tracker-control-circle";
    private const string _squareClass = "tracker-control-square";
    private const string _triangleClass = "tracker-control-triangle";
    private const string _leftStickLabel = "LS";
    private const string _rightStickLabel = "RS";

    /// <summary>
    /// Gets or sets the Windows virtual key, when this token represents the keyboard.
    /// </summary>
    [Parameter]
    public int? KeyCode { get; set; }

    /// <summary>
    /// Gets or sets the normalized controller token.
    /// </summary>
    [Parameter]
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether to use PlayStation button symbols.
    /// </summary>
    [Parameter]
    public bool PlayStation { get; set; }

    /// <summary>
    /// Gets the vector path for the directional arrow.
    /// </summary>
    private string? DirectionPath => KeyCode switch
    {
        0x26 => _upPath,
        0x28 => _downPath,
        0x25 => _leftPath,
        0x27 => _rightPath,
        _ => Token switch
        {
            ControlInput.LeftYNegative or ControlInput.RightYNegative or ControlInput.DpadUp => _upPath,
            ControlInput.LeftYPositive or ControlInput.RightYPositive or ControlInput.DpadDown => _downPath,
            ControlInput.LeftXNegative or ControlInput.RightXNegative or ControlInput.DpadLeft => _leftPath,
            ControlInput.LeftXPositive or ControlInput.RightXPositive or ControlInput.DpadRight => _rightPath,
            _ => null
        }
    };

    /// <summary>
    /// Gets whether the token represents the directional pad.
    /// </summary>
    private bool IsDirectionalPad => Token is ControlInput.DpadUp or ControlInput.DpadDown or ControlInput.DpadLeft or ControlInput.DpadRight;

    /// <summary>
    /// Gets whether the token represents clicking a stick.
    /// </summary>
    private bool IsStickClick => Token is ControlInput.LeftStick or ControlInput.RightStick;

    /// <summary>
    /// Gets whether the token represents the menu button.
    /// </summary>
    private bool IsMenu => Token == ControlInput.Start;

    /// <summary>
    /// Gets the text accompanying any directional symbol.
    /// </summary>
    private string Label => KeyCode is int key ? (DirectionPath is null ? ControlNames.Keyboard(key) : string.Empty) : Token switch
    {
        ControlInput.LeftXNegative or ControlInput.LeftXPositive or ControlInput.LeftYNegative or ControlInput.LeftYPositive => _leftStickLabel,
        ControlInput.RightXNegative or ControlInput.RightXPositive or ControlInput.RightYNegative or ControlInput.RightYPositive => _rightStickLabel,
        ControlInput.LeftStick when !PlayStation => _leftStickLabel,
        ControlInput.RightStick when !PlayStation => _rightStickLabel,
        _ => ControlNames.Controller(Token, PlayStation)
    };

    /// <summary>
    /// Gets the PlayStation face-button vector path.
    /// </summary>
    private string? FacePath => !PlayStation ? null : Token switch
    {
        ControlInput.South => _crossPath,
        ControlInput.East => _circlePath,
        ControlInput.West => _squarePath,
        ControlInput.North => _trianglePath,
        _ => null
    };

    /// <summary>
    /// Gets the optical size class for a face-button symbol.
    /// </summary>
    private string? FaceClass => Token switch
    {
        ControlInput.South => _crossClass,
        ControlInput.East => _circleClass,
        ControlInput.West => _squareClass,
        ControlInput.North => _triangleClass,
        _ => null
    };
}
