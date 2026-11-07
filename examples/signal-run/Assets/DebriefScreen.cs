using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Original, asset-free uGUI scene for the reference/replay example.
public sealed class DebriefScreen : MonoBehaviour
{
    private Font font;
    private static readonly Color32 Ink = new Color32(15, 28, 32, 255);
    private static readonly Color32 Paper = new Color32(226, 233, 214, 255);
    private static readonly Color32 Lime = new Color32(193, 244, 99, 255);

    private static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.sizeDelta = new Vector2(w, h);
        rect.anchoredPosition = new Vector2(x, y);
        return rect;
    }

    private static Image Block(Transform parent, string name, float x, float y, float w, float h, Color color)
    {
        var image = Rect(parent, name, x, y, w, h).gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private Text Label(Transform parent, string value, float x, float y, float w, float h, int size, Color color,
                       FontStyle style = FontStyle.Normal)
    {
        var text = Rect(parent, value, x, y, w, h).gameObject.AddComponent<Text>();
        text.font = font;
        text.text = value;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = TextAnchor.MiddleLeft;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }

    private void Start()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var screen = new GameObject("Debrief", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        screen.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = screen.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(640, 360);
        scaler.matchWidthOrHeight = .5f;
        var bg = Block(screen.transform, "Backdrop", 0, 0, 640, 360, Ink).transform;
        var grid = new Color32(35, 51, 54, 255);
        for (int x = -320; x <= 320; x += 32) Block(bg, "Grid", x, 0, 1, 360, grid);
        for (int y = -180; y <= 180; y += 32) Block(bg, "Grid", 0, y, 640, 1, grid);
        Label(bg, "SIGNAL RUN", -215, 147, 120, 22, 15, Lime, FontStyle.Bold);
        Label(bg, "MISSION CONTROL", 204, 147, 130, 22, 12, Paper);

        var card = Block(screen.transform, "Result", 0, 12, 400, 164, Paper).transform;
        Label(card, "FIELD REPORT   /   07", -81, 53, 194, 22, 13, Ink);
        Block(card, "Rule", -68, 32, 220, 1, Ink);
        Label(card, "Relay online.", -58, 0, 242, 54, 36, Ink, FontStyle.Bold);
        Label(card, "North ridge station", -73, -47, 210, 26, 16, Ink);
        var map = Block(card, "Relay map", 127, 0, 114, 136, Ink).transform;
        for (int i = 0; i < 3; i++)
        {
            float side = 58 - 18 * i;
            var loop = Rect(map, "Orbit", 0, 6, side, side);
            loop.localRotation = Quaternion.Euler(0, 0, 45);
            Color32 c = i == 2 ? Lime : new Color32(106, 145, 111, 255);
            Block(loop, "West", -side/2, 0, 2, side, c);
            Block(loop, "East", side/2, 0, 2, side, c);
            Block(loop, "North", 0, side/2, side, 2, c);
            Block(loop, "South", 0, -side/2, side, 2, c);
        }
        Label(map, "LINK  100%", -1, -50, 82, 16, 11, Lime);
        var action = Block(screen.transform, "Continue", 0, -103, 400, 37, Lime);
        action.raycastTarget = true;
        var label = Label(action.transform, "CONTINUE", -81, 0, 194, 24, 14, Ink, FontStyle.Bold);
        Label(action.transform, ">", 170, 0, 26, 24, 18, Ink, FontStyle.Bold);
        var button = action.gameObject.AddComponent<Button>();
        button.targetGraphic = action;
        button.onClick.AddListener(() => label.text = "ACKNOWLEDGED");
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }
}
