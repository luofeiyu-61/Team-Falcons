#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using TJGenerators.Config;
using TJGenerators.UI;
using TJGenerators.Utils;

namespace TJGenerators
{
    /// <summary>
    /// 选择纹理走势 - 预览窗口（新 UI 风格）
    /// </summary>
    public class TJGeneratorsTexturePatternSelectorPreviewWindow : EditorWindow
    {
        private static string AllTag => TJGeneratorsL10n.L("全部");

        private const float WindowWidth = 584f;
        private const float WindowHeight = 686f;

        private const float SearchTop = 20f;
        private const float ContentLeft = 20f;
        private const float ContentRight = 20f; // 结合 544 固定宽，右侧留白为 20
        private const float SearchWidth = 544f;
        private const float SearchHeight = 39f;

        private const float TagTop = 79f;
        private const float TagButtonHeight = 36f;
        private const float TagButtonGap = 10f;
        private const float TagButtonMinWidth = 65f;
        private const float TagButtonLongWidth = 135f;
        private const float CardWidth = 138f;
        private const float CardHeight = 169f;
        private const float CardGap = 10f;
        private const float LabelToCardsGap = 10f;
        private const float SectionGap = 30f;

        private List<MaterialTemplateOptionConfig> _templates;
        private Action<MaterialTemplateOptionConfig> _onSelected;
        private readonly Dictionary<string, Texture2D> _previewCache = new Dictionary<string, Texture2D>();
        private Vector2 _scrollPosition;
        private string _searchText = string.Empty;
        private string _selectedTag = AllTag;
        private string _windowTitle = null;
        private string _currentSelectedId = string.Empty;

        private GUIStyle _tagTextStyle;
        private GUIStyle _sectionLabelStyle;
        private GUIStyle _nameStyle;
        private GUIStyle _descStyle;

        public static void ShowWindow(
            List<MaterialTemplateOptionConfig> templates,
            Action<MaterialTemplateOptionConfig> onSelected,
            string title = null,
            MaterialTemplateOptionConfig currentSelected = null)
        {
            var resolvedTitle = title ?? TJGeneratorsL10n.L("选择纹理走势");
            var window = GetWindow<TJGeneratorsTexturePatternSelectorPreviewWindow>(resolvedTitle);
            window.minSize = new Vector2(WindowWidth, WindowHeight);
            window.maxSize = new Vector2(WindowWidth, WindowHeight);
            window._templates = templates;
            window._onSelected = onSelected;
            window._windowTitle = resolvedTitle;
            window._currentSelectedId = currentSelected != null ? currentSelected.id : string.Empty;
            window.LoadPreviews();
            window.Show();
        }

        private void LoadPreviews()
        {
            _previewCache.Clear();
            if (_templates == null) return;
            foreach (var template in _templates)
            {
                if (template == null || string.IsNullOrEmpty(template.id))
                    continue;
                LoadPreview(template.id);
            }
        }

        private void LoadPreview(string templateId)
        {
            if (_previewCache.ContainsKey(templateId))
                return;

            string templatePath = TJGeneratorsMaterialTemplateGenerator.GetTemplateImagePath(templateId);
            Texture2D tex = EditorGUIUtility.Load(templatePath) as Texture2D;

            if (tex == null)
            {
                string absolutePath = TJGeneratorsMaterialTemplateGenerator.GetAbsoluteTemplatePath(templateId);
                if (File.Exists(absolutePath))
                {
                    tex = new Texture2D(2, 2);
                    tex.LoadImage(File.ReadAllBytes(absolutePath));
                }
            }

            _previewCache[templateId] = tex;
        }

        private void EnsureStyles()
        {
            if (_tagTextStyle != null) return;

            _tagTextStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                font = CommonStyles.SourceHanSansRegularFont,
                fontSize = 12,
                fontStyle = FontStyle.Normal,
                clipping = TextClipping.Clip,
                wordWrap = false
            };

            _sectionLabelStyle = new GUIStyle(EditorStyles.label)
            {
                font = CommonStyles.SourceHanSansMediumFont != null ? CommonStyles.SourceHanSansMediumFont : CommonStyles.SourceHanSansRegularFont,
                fontSize = 16,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleLeft
            };
            _sectionLabelStyle.normal.textColor = Color.white;

            _nameStyle = new GUIStyle(EditorStyles.label)
            {
                font = CommonStyles.SourceHanSansRegularFont,
                fontSize = 12,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleCenter
            };
            _nameStyle.normal.textColor = Color.white;

            _descStyle = new GUIStyle(EditorStyles.label)
            {
                font = CommonStyles.SourceHanSansRegularFont,
                fontSize = 10,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleCenter
            };
            _descStyle.normal.textColor = new Color(128f / 255f, 128f / 255f, 128f / 255f, 1f);
        }

        private void OnGUI()
        {
            UIComponents.SyncImguiLeftMouseHeldFromEvent();

            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(0f, 0f, position.width, position.height), CommonStyles.WindowBackgroundColor);

            EnsureStyles();

            DrawSearchBar();
            DrawTagRow();
            DrawGroupedCards();
        }

        private void DrawSearchBar()
        {
            GUILayout.Space(SearchTop);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(ContentLeft);
            string newSearch = UIComponents.DrawSearchTextField(
                _searchText,
                TJGeneratorsL10n.L("输入关键词搜索..."),
                GUILayout.Width(SearchWidth),
                GUILayout.MinWidth(SearchWidth),
                GUILayout.MaxWidth(SearchWidth),
                GUILayout.Height(SearchHeight));
            if (newSearch != _searchText)
            {
                _searchText = newSearch;
                Repaint();
            }
            GUILayout.Space(ContentRight);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTagRow()
        {
            float spacer = Mathf.Max(0f, TagTop - (SearchTop + SearchHeight));
            if (spacer > 0f) GUILayout.Space(spacer);

            var tags = BuildTags();
            if (tags.Count == 0) return;

            float x = ContentLeft;
            float y = 0f;
            float maxX = position.width - ContentRight;

            Texture2D black = CommonStyles.BlackBtnNormal4xTexture;
            Texture2D greenN = CommonStyles.GenerateButtonSolidStyle.normal.background;
            Texture2D greenH = CommonStyles.GenerateButtonSolidStyle.hover.background ?? greenN;
            Texture2D greenP = CommonStyles.GenerateButtonSolidStyle.active.background ?? greenH;

            Rect rowRect = GUILayoutUtility.GetRect(position.width, TagButtonHeight, GUILayout.Height(TagButtonHeight));
            y = rowRect.y;

            foreach (var label in tags)
            {
                bool isSelected = string.Equals(_selectedTag, label, StringComparison.OrdinalIgnoreCase);
                float w = label.Length >= 10 ? TagButtonLongWidth : TagButtonMinWidth;
                if (x + w > maxX) break;

                Rect rect = new Rect(Mathf.Floor(x), Mathf.Floor(y), Mathf.Floor(w), TagButtonHeight);
                bool isHover = rect.Contains(Event.current.mousePosition);
                bool isPressing = isHover && UIComponents.ImguiLeftMouseHeld;

                Texture2D bg;
                if (isSelected)
                    bg = isPressing ? greenP : (isHover ? greenH : greenN);
                else
                    bg = isPressing ? greenP : (isHover ? greenH : black);

                if (bg != null)
                    UIComponents.DrawNineSliceFixed(rect, bg, 32, 8);

                _tagTextStyle.normal.textColor = isSelected ? Color.white : new Color(216f / 255f, 216f / 255f, 216f / 255f, 1f);
                GUI.Label(rect, label, _tagTextStyle);

                EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
                if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
                {
                    _selectedTag = label;
                    _scrollPosition = Vector2.zero;
                    Repaint();
                    Event.current.Use();
                }

                x += w + TagButtonGap;
            }

            GUILayout.Space(20f); // 与模型选择窗口一致的按钮区到下方内容间距
        }

        private List<string> BuildTags()
        {
            var list = new List<string> { AllTag };
            if (_templates == null) return list;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in _templates)
            {
                if (t == null || string.IsNullOrEmpty(t.category)) continue;
                string cat = TJGeneratorsL10n.L(t.category);
                if (seen.Add(cat)) list.Add(cat);
            }
            return list;
        }

        private IEnumerable<MaterialTemplateOptionConfig> FilteredTemplates()
        {
            if (_templates == null) yield break;
            string keyword = (_searchText ?? string.Empty).Trim();
            foreach (var t in _templates)
            {
                if (t == null) continue;
                if (_selectedTag != AllTag && !string.Equals(TJGeneratorsL10n.L(t.category), _selectedTag, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.IsNullOrEmpty(keyword))
                {
                    string k = keyword.ToLowerInvariant();
                    if (!ContainsIgnoreCase(TJGeneratorsL10n.L(t.name), k) &&
                        !ContainsIgnoreCase(TJGeneratorsL10n.L(t.description), k) &&
                        !ContainsIgnoreCase(TJGeneratorsL10n.L(t.category), k) &&
                        !ContainsIgnoreCase(t.id, k))
                        continue;
                }
                yield return t;
            }
        }

        private static bool ContainsIgnoreCase(string s, string keywordLower)
        {
            if (string.IsNullOrEmpty(s) || string.IsNullOrEmpty(keywordLower)) return false;
            return s.ToLowerInvariant().Contains(keywordLower);
        }

        private void DrawGroupedCards()
        {
            var filtered = FilteredTemplates().OrderBy(t => t.order).ThenBy(t => t.name).ToList();
            var grouped = filtered
                .GroupBy(t => string.IsNullOrEmpty(t.category) ? TJGeneratorsL10n.L("其他") : TJGeneratorsL10n.L(t.category))
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            float scrollHeight = Mathf.Max(120f, position.height - (TagTop + TagButtonHeight + 20f + 20f));
            _scrollPosition = GUILayout.BeginScrollView(
                _scrollPosition,
                false,
                true,
                GUIStyle.none,
                GUI.skin.verticalScrollbar,
                GUILayout.Height(scrollHeight));

            if (grouped.Count == 0)
            {
                GUILayout.Space(20f);
                GUILayout.Label(TJGeneratorsL10n.L("当前分类下没有选项"), CommonStyles.SmallGreyCenterLabelStyle);
                GUILayout.EndScrollView();
                return;
            }

            int cardsPerRow = 3; // 584 固定宽下稳定三列
            foreach (var g in grouped)
            {
                GUILayout.Space(2f);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(ContentLeft);
                GUILayout.Label(TJGeneratorsL10n.L(g.Key), _sectionLabelStyle, GUILayout.Height(20f));
                GUILayout.Space(ContentRight);
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(LabelToCardsGap);

                var items = g.ToList();
                int index = 0;
                while (index < items.Count)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(ContentLeft);
                    int rowCount = Mathf.Min(cardsPerRow, items.Count - index);
                    for (int i = 0; i < rowCount; i++)
                    {
                        DrawTextureCard(items[index++]);
                        if (i < rowCount - 1)
                            GUILayout.Space(CardGap);
                    }
                    GUILayout.FlexibleSpace();
                    GUILayout.Space(ContentRight);
                    EditorGUILayout.EndHorizontal();
                    GUILayout.Space(CardGap);
                }

                GUILayout.Space(SectionGap - CardGap); // 最后一行后已经有一次 CardGap，这里补足到 30
            }

            GUILayout.EndScrollView();
        }

        private void DrawTextureCard(MaterialTemplateOptionConfig template)
        {
            bool isSelected = !string.IsNullOrEmpty(_currentSelectedId) &&
                              string.Equals(_currentSelectedId, template.id, StringComparison.OrdinalIgnoreCase);
            Rect cardRect = GUILayoutUtility.GetRect(CardWidth, CardHeight, GUILayout.Width(CardWidth), GUILayout.Height(CardHeight));

            Texture2D cardBg = isSelected ? CommonStyles.ItemBoxChecked4xTexture : CommonStyles.ItemBoxNormal4xTexture;
            if (cardBg != null)
                UIComponents.DrawNineSliceFixed(cardRect, cardBg, 16, 4);
            else
                EditorGUI.DrawRect(cardRect, new Color(26f / 255f, 26f / 255f, 26f / 255f, 1f));

            Rect imageRect = new Rect(cardRect.x + 10f, cardRect.y + 10f, 118f, 101f);
            if (_previewCache.TryGetValue(template.id, out var tex) && tex != null)
            {
                Rect shadowRect = new Rect(imageRect.x, imageRect.y + 2f, imageRect.width, imageRect.height);
                EditorGUI.DrawRect(shadowRect, new Color(0f, 0f, 0f, 0.25f));
                GUI.DrawTexture(imageRect, tex, ScaleMode.ScaleToFit, true);
            }
            else
            {
                EditorGUI.DrawRect(imageRect, new Color(0.2f, 0.2f, 0.2f, 1f));
                GUI.Label(imageRect, TJGeneratorsL10n.L("无预览"), CommonStyles.SmallGreyCenterLabelStyle);
            }

            Rect nameRect = new Rect(cardRect.x + 10f, cardRect.y + 123f, cardRect.width - 20f, 16f);
            Rect descRect = new Rect(cardRect.x + 10f, cardRect.y + 142f, cardRect.width - 20f, 20f);
            GUI.Label(nameRect, TJGeneratorsL10n.L(template.name ?? string.Empty).ToUpperInvariant(), _nameStyle);
            GUI.Label(descRect, TJGeneratorsL10n.L(template.description ?? string.Empty).ToUpperInvariant(), _descStyle);

            EditorGUIUtility.AddCursorRect(cardRect, MouseCursor.Link);
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && cardRect.Contains(Event.current.mousePosition))
            {
                _onSelected?.Invoke(template);
                Event.current.Use();
                Close();
            }
        }
    }
}
#endif
