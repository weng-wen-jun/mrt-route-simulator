using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

/// <summary>原始 JSON 文字編輯器已退役；正常 Schema 8 UI 一律進入 topology-first 工作區。</summary>
[Obsolete("請改用 TopologyEditorWindow。", error: false)]
internal static class TopologyJsonEditorWindow
{
    public static TopologyEditorWindow Create(TopologyProjectDocument document) => new(document);
}
