#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Linq;

public class SimpleBlendshapeRenamerWindow : EditorWindow
{
    private SkinnedMeshRenderer smr;
    private Mesh srcMesh;
    private int selectedIndex = -1;
    private string newName = "";
    private string[] bsNames = new string[0];

    private string search = "";
    private Vector2 scroll;
    private string saveFolder = "Assets/EditedMeshes";
    private bool autoAssign = true;

    [MenuItem("Tools/Blendshape Simple Renamer")]
    static void Open() => GetWindow<SimpleBlendshapeRenamerWindow>("Blendshape Renamer");

    private void OnGUI()
    {
        // 1) 选择 SMR
        smr = (SkinnedMeshRenderer)EditorGUILayout.ObjectField("SkinnedMeshRenderer", smr, typeof(SkinnedMeshRenderer), true);
        if (smr && (srcMesh != smr.sharedMesh))
        {
            srcMesh = smr.sharedMesh;
            ReloadNames();
            selectedIndex = -1;
            newName = "";
        }
        if (!smr || !srcMesh)
        {
            EditorGUILayout.HelpBox("选择一个带 BlendShapes 的 SkinnedMeshRenderer。", MessageType.Info);
            return;
        }

        // 2) 选一行（支持搜索 + 可滚动列表）
        EditorGUILayout.Space(6);
        search = EditorGUILayout.TextField("搜索", search);

        EditorGUILayout.LabelField($"BlendShapes: {srcMesh.blendShapeCount}");
        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(160), GUILayout.MaxHeight(320));
        for (int i = 0; i < bsNames.Length; i++)
        {
            string name = bsNames[i];
            if (!string.IsNullOrEmpty(search) &&
                name.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0) continue;

            EditorGUILayout.BeginHorizontal();
            bool isSel = (i == selectedIndex);
            if (GUILayout.Toggle(isSel, "", GUILayout.Width(18)) != isSel)
            {
                selectedIndex = i;
                newName = name; // 默认填入旧名，便于微改
            }

            GUILayout.Label(i.ToString("000"), GUILayout.Width(40));
            GUILayout.Label(name);
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        // 当前选择 & 新名字
        EditorGUILayout.Space(6);
        using (new EditorGUI.DisabledScope(selectedIndex < 0))
        {
            EditorGUILayout.LabelField("当前选择", selectedIndex >= 0 ? $"{selectedIndex:000}  {bsNames[selectedIndex]}" : "-");
            newName = EditorGUILayout.TextField("新名称", newName);
        }

        // 输出选项
        EditorGUILayout.Space(6);
        saveFolder = EditorGUILayout.TextField("保存目录", saveFolder);
        autoAssign = EditorGUILayout.ToggleLeft("重命名后自动挂回原对象", autoAssign);

        // 执行
        using (new EditorGUI.DisabledScope(selectedIndex < 0 || !IsValidNewName(newName, selectedIndex)))
        {
            if (GUILayout.Button("重命名（生成新 Mesh）", GUILayout.Height(28)))
                RenameOne(selectedIndex, newName.Trim());
        }

        // 错误提示
        if (selectedIndex >= 0 && !IsValidNewName(newName, selectedIndex))
        {
            EditorGUILayout.HelpBox("新名称为空或与其他形态键重名。", MessageType.Error);
        }
    }

    private void ReloadNames()
    {
        if (!srcMesh) { bsNames = new string[0]; return; }
        bsNames = Enumerable.Range(0, srcMesh.blendShapeCount)
                            .Select(i => srcMesh.GetBlendShapeName(i))
                            .ToArray();
    }

    private bool IsValidNewName(string name, int selfIndex)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        for (int i = 0; i < bsNames.Length; i++)
            if (i != selfIndex && bsNames[i] == name) return false;
        return true;
    }

    private void RenameOne(int index, string targetName)
    {
        // 复制 Mesh
        var dst = Object.Instantiate(srcMesh);
        dst.name = srcMesh.name + "_Renamed";

        int bsCount = dst.blendShapeCount;
        int vtx = dst.vertexCount;

        // 缓存所有帧，并把指定 index 的名字替换为新名
        var cached = new System.Collections.Generic.List<(string name, System.Collections.Generic.List<(float w, Vector3[] dv, Vector3[] dn, Vector3[] dt)>)>(bsCount);
        for (int i = 0; i < bsCount; i++)
        {
            string name = (i == index) ? targetName : dst.GetBlendShapeName(i);
            int frameCount = dst.GetBlendShapeFrameCount(i);
            var frames = new System.Collections.Generic.List<(float, Vector3[], Vector3[], Vector3[])>(frameCount);
            for (int f = 0; f < frameCount; f++)
            {
                float w = dst.GetBlendShapeFrameWeight(i, f);
                var dv = new Vector3[vtx];
                var dn = new Vector3[vtx];
                var dt = new Vector3[vtx];
                dst.GetBlendShapeFrameVertices(i, f, dv, dn, dt);
                frames.Add((w, dv, dn, dt));
            }
            cached.Add((name, frames));
        }

        // 重建 BlendShapes
        dst.ClearBlendShapes();
        foreach (var (name, frames) in cached)
            foreach (var (w, dv, dn, dt) in frames)
                dst.AddBlendShapeFrame(name, w, dv, dn, dt);

        // 保存新资产
        EnsureFolder(saveFolder);
        string path = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(saveFolder, dst.name + ".asset"));
        AssetDatabase.CreateAsset(dst, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // 自动挂回
        if (autoAssign)
        {
            Undo.RecordObject(smr, "Assign Renamed Mesh");
            smr.sharedMesh = dst;
            srcMesh = dst;
            ReloadNames();
        }

        EditorUtility.DisplayDialog("完成", $"已生成：\n{path}\n已重命名 {index:000}: {bsNames[index]} → {targetName}", "OK");
    }

    private void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        var parent = Path.GetDirectoryName(folder).Replace("\\", "/");
        var leaf = Path.GetFileName(folder);
        if (string.IsNullOrEmpty(parent) || !AssetDatabase.IsValidFolder(parent)) parent = "Assets";
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
#endif
