/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * Licensed under the Oculus SDK License Agreement (the "License");
 * you may not use the Oculus SDK except in compliance with the License,
 * which is provided at the time of installation or download, or which
 * otherwise accompanies this software in either electronic or hard copy form.
 *
 * You may obtain a copy of the License at
 *
 * https://developer.oculus.com/licenses/oculussdk/
 *
 * Unless required by applicable law or agreed to in writing, the Oculus SDK
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

#if UNITY_2021_2_OR_NEWER
#define OVR_BB_DRAGANDDROP
#endif

using System;
using System.Collections.Generic;
using System.Linq;
using Meta.XR.Editor.Tags;
using UnityEditor;
using UnityEngine;

namespace Meta.XR.BuildingBlocks.Editor
{
    public class BuildingBlocksWindow : EditorWindow
    {
        private const string MenuPath = "Oculus/Tools/Building Blocks";
        private const int MenuPriority = 2;
        private static readonly string WindowName = Utils.BlocksPublicName;
        private const string AddButtonLabel = "Add";

#if OVR_BB_DRAGANDDROP
        private const string DragAndDropLabel = "Dragging Block";
        private const string DragAndDropBlockDataLabel = "block";
        private const string DragAndDropBlockThumbnailLabel = "blockThumbnail";
#endif // OVR_BB_DRAGANDDROP

        private static readonly GUIContent Title = new GUIContent(Utils.BlocksPublicName);

        private static readonly GUIContent Description =
            new GUIContent("Drag and drop blocks onto your scene to add XR features to your project.");

        private const string DocumentationUrl =
        "https://developer.oculus.com/documentation/unity/unity-buildingblocks-overview";


        private Vector2 _scrollPosition;

        private OVRAnimatedContent _outline = null;
        private OVRAnimatedContent _tutorial = null;
        private static readonly OVRProjectSetupSettingBool _tutorialCompleted =
            new OVRProjectSetupUserSettingBool("BuildingBlocksTutorialCompleted", false);
        private static bool _shouldShowTutorial = false;
        private bool isHoveringHotControl = false;

        private HashSet<Tag> _tagSearch = new HashSet<Tag>();
        private string _filterSearch = "";

        [MenuItem(MenuPath, false, MenuPriority)]
        private static void ShowWindow()
        {
            ShowWindow("MenuItem");
        }

        public static void ShowWindow(string source)
        {
            var window = GetWindow<BuildingBlocksWindow>(WindowName);
            window.minSize = new Vector2(800, 400);

            OVRTelemetry.Start(OVRTelemetryConstants.BB.MarkerId.OpenWindow)
                .AddAnnotation(OVRTelemetryConstants.BB.AnnotationType.ActionTrigger, source)
                .Send();
        }

        private void OnGUI()
        {
            OnHeaderGUI();

            isHoveringHotControl = false;

            ComputeBlockSize(out var numberOfColumns, out var windowWidth, out var expectedThumbnailWidth, out var expectedThumbnailHeight);

            ShowList(numberOfColumns, expectedThumbnailWidth, expectedThumbnailHeight, windowWidth);
#if OVR_BB_DRAGANDDROP
            RefreshDragAndDrop(expectedThumbnailWidth, expectedThumbnailHeight);
#endif // OVR_BB_DRAGANDDROP

            if (Event.current.type == EventType.MouseMove)
            {
                Repaint();
            }
        }

        private int _previousThumbnailWidth = 0;
        private int _previousThumbnailHeight = 0;

        private void ComputeBlockSize(out int numberOfColumns, out int windowWidth, out int width, out int height)
        {
            windowWidth = (int)position.width - Styles.BlockMargin;
            var blockWidth = Styles.IdealThumbnailWidth;
            windowWidth = Mathf.Max(Styles.IdealThumbnailWidth + Styles.Padding * 3, windowWidth);
            var scrollableAreaWidth = windowWidth - 18;
            numberOfColumns = Mathf.FloorToInt(scrollableAreaWidth / blockWidth);
            if (numberOfColumns < 1) numberOfColumns = 1;
            var marginToRemove = numberOfColumns * Styles.BlockMargin;

            width = (int)Mathf.FloorToInt((scrollableAreaWidth - marginToRemove) / numberOfColumns);
            height = (int)Mathf.FloorToInt(width / Styles.ThumbnailRatio);
            if (width != _previousThumbnailWidth || height != _previousThumbnailHeight)
            {
                _previousThumbnailWidth = width;
                _previousThumbnailHeight = height;
                OVREditorUtils.TweenHelper.Reset();
            }
        }

        private void OnHeaderGUI()
        {
            EditorGUILayout.BeginHorizontal(Styles.Header);
            {
                using (new OVREditorUtils.OVRGUIColorScope(OVREditorUtils.OVRGUIColorScope.Scope.Content, Styles.Colors.AccentColor))
                {
                    EditorGUILayout.LabelField(Styles.HeaderIcon, Styles.HeaderIconStyle, GUILayout.Width(32.0f),
                        GUILayout.ExpandWidth(false));
                }
                EditorGUILayout.LabelField(Title, Styles.BoldLabel);

                EditorGUILayout.Space(0, true);
                if (GUILayout.Button(Styles.ConfigIcon, Styles.MiniButton))
                {
                    ShowSettingsMenu();
                }

                if (GUILayout.Button(Styles.DocumentationIcon, Styles.MiniButton))
                {
                    Application.OpenURL(DocumentationUrl);
                }

            }
            EditorGUILayout.EndHorizontal();

            if (!OVREditorUtils.IsUnityVersionCompatible())
            {
                using (new OVREditorUtils.OVRGUIColorScope(OVREditorUtils.OVRGUIColorScope.Scope.Background, Styles.Colors.ErrorColorSemiTransparent))
                {
                    EditorGUILayout.LabelField(
                        $"<b>Warning:</b> Your version of Unity is not supported. Consider upgrading to {OVREditorUtils.VersionCompatible} or higher.",
                        Styles.ErrorHelpBox);
                }
            }

            EditorGUILayout.BeginHorizontal(Styles.SubtitleHelpText);
            GUILayout.Label(Description, Styles.LabelStyle);
            EditorGUILayout.EndHorizontal();
        }


        private void ShowSettingsMenu()
        {
            var menu = new GenericMenu();
            foreach (var tag in Tag.Registry)
            {
                if (tag.Behavior.ToggleableVisibility)
                {
                    tag.Behavior.VisibilitySetting.AppendToMenu(menu, ClearTagSearch);
                }
            }
            menu.ShowAsContext();
        }

        private void ClearTagSearch()
        {
            _tagSearch.Clear();
        }

        private void RefreshShowTutorial()
        {
            _shouldShowTutorial = ShouldShowTutorial();
        }

        private void OnEnable()
        {
            RefreshBlockList();
#if OVR_BB_DRAGANDDROP
            DragAndDrop.AddDropHandler(SceneDropHandler);
            DragAndDrop.AddDropHandler(HierarchyDropHandler);
#endif // OVR_BB_DRAGANDDROP
            wantsMouseMove = true;
            RefreshShowTutorial();
        }

        private void RefreshBlockList()
        {
            _blockList = GetList();
        }

        private void OnDisable()
        {
#if OVR_BB_DRAGANDDROP
            DragAndDrop.RemoveDropHandler(SceneDropHandler);
            DragAndDrop.RemoveDropHandler(HierarchyDropHandler);
#endif // OVR_BB_DRAGANDDROP
        }

        private List<BlockBaseData> _blockList;

        private static List<BlockBaseData> GetList()
        {
            var blockGuids = AssetDatabase.FindAssets($"t:{nameof(BlockBaseData)}");

            return blockGuids.Select(id =>
                    AssetDatabase.LoadAssetAtPath<BlockBaseData>(AssetDatabase.GUIDToAssetPath(id)))
                .Where(obj => !string.IsNullOrEmpty(obj.name))
                .OrderBy(block => block.Order)
                .ThenBy(block => block.BlockName)
                .ToList();
        }

        private void ShowList(int numberOfColumns, int expectedThumbnailWidth, int expectedThumbnailHeight, float expectedScrollWidth)
        {
            GUILayout.BeginHorizontal(Styles.FilterByLine);
            EditorGUILayout.LabelField("Filter by", EditorStyles.miniBoldLabel, GUILayout.Width(44));
            ShowTagList("window", Tag.Registry.SortedTags, _tagSearch, Tag.TagListType.Filters);
            EditorGUILayout.Space(0, true);
            _filterSearch = EditorGUILayout.TextField(_filterSearch, GUI.skin.FindStyle("SearchTextField"), GUILayout.Width(256));
            GUILayout.EndHorizontal();

            ShowList(_blockList, Filter, numberOfColumns, expectedThumbnailWidth, expectedThumbnailHeight, expectedScrollWidth);
        }

        private IEnumerable<BlockBaseData> Filter(IEnumerable<BlockBaseData> blocks) => blocks.Where(Match);

        private bool Match(BlockBaseData block)
        {
            var hasTag = _tagSearch.All(tag => block.Tags.Contains(tag));
            var containsSearch = string.IsNullOrEmpty(_filterSearch)
                           || block.blockName.Contains(_filterSearch)
                           || block.Description.Contains(_filterSearch)
                           || block.Tags.Any(tag => tag.Name.Contains(_filterSearch));
            var hasHiddenTag = block.Tags.Any(tag => tag.Behavior.Visibility == false);
            return hasTag && containsSearch && !hasHiddenTag;
        }

        private void ShowList(List<BlockBaseData> blocks, Func<IEnumerable<BlockBaseData>,
                IEnumerable<BlockBaseData>> filter, int numberOfColumns, int expectedThumbnailWidth,
            int expectedThumbnailHeight, float expectedScrollWidth)
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, false, true, GUIStyle.none, GUI.skin.verticalScrollbar, Styles.NoMargin, GUILayout.Width(expectedScrollWidth));

            var blockWidth = expectedThumbnailWidth;
            var blockHeight = expectedThumbnailHeight + Styles.DescriptionAreaStyle.fixedHeight + 3;

            var columnIndex = 0;
            var lineIndex = 0;
            var showTutorial = _shouldShowTutorial;
            GUILayout.BeginHorizontal(Styles.NoMargin);
            var filteredBlocks = filter(blocks);
            foreach (var block in filteredBlocks)
            {
                var blockRect = new Rect(columnIndex * (blockWidth + Styles.BlockMargin) + Styles.BlockMargin, lineIndex * (blockHeight + Styles.BlockMargin), blockWidth, blockHeight);
                Show(block, blockRect, expectedThumbnailWidth, expectedThumbnailHeight);

                if (showTutorial && block.CanBeAdded)
                {
                    ShowTutorial(blockRect);
                    showTutorial = false;
                }

                columnIndex++;
                if (columnIndex >= numberOfColumns)
                {
                    lineIndex++;
                    columnIndex = 0;
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal(Styles.NoMargin);
                }
            }

            GUILayout.EndHorizontal();

            EditorGUILayout.EndScrollView();
        }

        private void ShowThumbnail(BlockBaseData block, float targetHeight, int expectedThumbnailHeight)
        {
            var thumbnailAreaStyle = new GUIStyle(Styles.ThumbnailAreaStyle);
            thumbnailAreaStyle.fixedHeight = targetHeight;
            var thumbnailArea = EditorGUILayout.BeginVertical(thumbnailAreaStyle, GUILayout.Height(thumbnailAreaStyle.fixedHeight));
            {
                thumbnailArea.height = expectedThumbnailHeight;
                GUI.DrawTexture(thumbnailArea, block.Thumbnail, ScaleMode.ScaleAndCrop);

                var hasAttributes = ShowTagList(block.Id + "overlay", block.Tags, _tagSearch, Tag.TagListType.Overlays);
                if (!hasAttributes)
                {
                    // This space fills the area, otherwise the area will have a height of null
                    // despite the fixedHeight set
                    EditorGUILayout.Space();
                }
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(Styles.SeparatorAreaStyle);
            {
                // This space fills the area, otherwise the area will have a height of null
                // despite the fixedHeight set
                EditorGUILayout.Space();
            }
            EditorGUILayout.EndVertical();
        }

        private void ShowButtons(BlockBaseData block, Rect blockRect, bool canBeAdded, bool canBeSelected)
        {
            GUILayout.BeginArea(blockRect, Styles.LargeButtonArea);
            GUILayout.FlexibleSpace();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            var blockData = block as BlockData;
            if (canBeAdded)
            {
                var addIcon = Styles.AddIcon;
                if (ShowLargeButton(block.Id, addIcon))
                {
                    Undo.IncrementCurrentGroup();
                    Undo.SetCurrentGroupName($"{block.BlockName} block creation");
                    block.AddToProject(null, block.RequireListRefreshAfterInstall ? RefreshBlockList : (Action)null);
                    Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
                }
            }

            if (canBeSelected)
            {
                if (ShowLargeButton(block.Id, Styles.SelectIcon))
                {
                    blockData.SelectBlocksInScene();
                }
            }

            EditorGUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void ShowDescription(BlockBaseData block, Rect blockRect, float targetHeight, int expectedThumbnailWidth, int expectedThumbnailHeight, bool canBeAdded, bool canBeSelected)
        {
            var blockData = block as BlockData;

            var hoverDescription = OVREditorUtils.HoverHelper.IsHover(block.Id + "Description");
            var descriptionStyle = new GUIStyle(hoverDescription ? Styles.DescriptionAreaHoverStyle : Styles.DescriptionAreaStyle);
            descriptionStyle.fixedHeight += expectedThumbnailHeight - targetHeight;
            var descriptionArea = EditorGUILayout.BeginVertical(descriptionStyle);
            hoverDescription = OVREditorUtils.HoverHelper.IsHover(block.Id + "Description", Event.current, descriptionArea);
            EditorGUILayout.BeginHorizontal();
            var descriptionRect = blockRect;
            descriptionRect.y += targetHeight + 2;
            descriptionRect.height -= targetHeight + Styles.Padding + 2;
            GUILayout.BeginArea(descriptionRect);
            var numberOfIcons = 0;
            if (canBeAdded) numberOfIcons++;
            if (canBeSelected) numberOfIcons++;
            var iconWidth = Styles.LargeButton.fixedWidth + Styles.LargeButton.margin.horizontal;
            var padding = descriptionStyle.padding.horizontal;
            var style = new GUIStyle(Styles.EmptyAreaStyle);
            style.fixedWidth = expectedThumbnailWidth - padding - numberOfIcons * iconWidth;
            style.fixedHeight = descriptionStyle.fixedHeight;
            style.padding = new RectOffset(Styles.BlockMargin, Styles.BlockMargin, Styles.BlockMargin, Styles.BlockMargin);
            EditorGUILayout.BeginVertical(style);
            EditorGUILayout.BeginHorizontal();
            var labelStyle = hoverDescription ? Styles.LabelHoverStyle : Styles.LabelStyle;
            EditorGUILayout.LabelField(block.BlockName, labelStyle);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(block.Description, Styles.InfoStyle);
            EditorGUILayout.BeginHorizontal();
            ShowTagList(block.Id, block.Tags, _tagSearch, Tag.TagListType.Filters);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            GUILayout.EndArea();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

#if OVR_BB_DRAGANDDROP
        private void ShowDragAndDrop(BlockBaseData block, Rect blockRect, bool canBeAdded)
        {
            var hoverGrid = OVREditorUtils.HoverHelper.IsHover(block.Id + "Grid", Event.current, blockRect);
            if (canBeAdded)
            {
                if (hoverGrid)
                {
                    if (Event.current.type == EventType.Repaint && !isHoveringHotControl)
                    {
                        EditorGUIUtility.AddCursorRect(blockRect, MouseCursor.Pan);
                    }

                    if (Event.current.type == EventType.MouseDown)
                    {
                        SetDragAndDrop(block);
                    }
                }
            }
        }

#endif // OVR_BB_DRAGANDDROP

        private void Show(BlockBaseData block, Rect blockRect, int expectedThumbnailWidth, int expectedThumbnailHeight)
        {
            var blockData = block as BlockData;
            var canBeAdded = block.CanBeAdded;
            var numberInScene = blockData != null ? blockData.ComputeNumberOfBlocksInScene() : 0;
            var canBeSelected = numberInScene > 0;

            var expectedColor = canBeAdded ? Color.white : Styles.DisabledColor;
            using var color = new OVREditorUtils.OVRGUIColorScope(OVREditorUtils.OVRGUIColorScope.Scope.All, expectedColor);
            var gridStyle = new GUIStyle(Styles.GridItemStyleWithHover);
            var gridDisabledStyle = new GUIStyle(Styles.GridItemDisabledStyle);
            var gridItemStyle = canBeAdded ? gridStyle : gridDisabledStyle;
            gridItemStyle.fixedWidth = expectedThumbnailWidth;

            var isHover = OVREditorUtils.HoverHelper.IsHover(block.Id + "Description");
            var targetHeight = isHover ? expectedThumbnailHeight - Styles.DescriptionAreaStyle.fixedHeight : expectedThumbnailHeight;
            targetHeight = (int)OVREditorUtils.TweenHelper.GUISmooth(block.Id, targetHeight, ifNotCompletedDelegate: Repaint);

            if (isHover)
            {
                block.MarkAsSeen();
            }

            EditorGUILayout.BeginVertical(gridItemStyle);
            {
                ShowThumbnail(block, targetHeight, expectedThumbnailHeight);
                ShowDescription(block, blockRect, targetHeight, expectedThumbnailWidth, expectedThumbnailHeight, canBeAdded, canBeSelected);
                ShowButtons(block, blockRect, canBeAdded, canBeSelected);

#if OVR_BB_DRAGANDDROP
                ShowDragAndDrop(block, blockRect, canBeAdded);
#endif // OVR_BB_DRAGANDDROP
            }
            EditorGUILayout.EndVertical();
        }

        private bool ShowTagList(string controlId, IEnumerable<Tag> tagArray, HashSet<Tag> search, Tag.TagListType listType)
        {
            var any = false;
            foreach (var tag in tagArray)
            {
                any |= ShowTag(controlId + "list", tag, search, listType);
            }

            return any;
        }

        private bool ShowTag(string controlId, Tag tag, HashSet<Tag> search, Tag.TagListType listType)
        {
            var tagBehavior = tag.Behavior;
            if (!tagBehavior.Show)
            {
                return false;
            }

            if (!tagBehavior.Visibility)
            {
                return false;
            }

            switch (listType)
            {
                case Tag.TagListType.Filters when !tagBehavior.CanFilterBy:
                case Tag.TagListType.Overlays when !tagBehavior.ShowOverlay:
                    return false;
            }

            var style = tagBehavior.Icon != null ? Styles.TagStyleWithIcon : Styles.TagStyle;
            var backgroundColors = listType == Tag.TagListType.Overlays ? Styles.TagOverlayBackgroundColors : Styles.TagBackgroundColors;

            var tagContent = new GUIContent(tag.Name);
            var tagWidth = style.CalcSize(tagContent).x + 1;
            var rect = GUILayoutUtility.GetRect(tagContent, style, GUILayout.Width(tagWidth));
            Vector2 mousePosition = Event.current.mousePosition;
            var id = controlId + tag;
            var color = backgroundColors.GetColor(search.Contains(tag), OVREditorUtils.HoverHelper.IsHover(id));
            using (new OVREditorUtils.OVRGUIColorScope(OVREditorUtils.OVRGUIColorScope.Scope.Background, color))
            {
                using (new OVREditorUtils.OVRGUIColorScope(OVREditorUtils.OVRGUIColorScope.Scope.Content, tagBehavior.Color))

                {
                    if (OVREditorUtils.HoverHelper.Button(id, rect, tagContent, style, out var hover))
                    {
                        if (_tagSearch.Contains(tag))
                        {
                            _tagSearch.Remove(tag);
                        }
                        else
                        {
                            _tagSearch.Add(tag);
                        }
                    }

                    if (tagBehavior.Icon != null)
                    {
                        GUI.Label(rect, tagBehavior.Icon, Styles.TagIcon);
                    }

                    isHoveringHotControl |= hover;
                }
            }
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);

            return true;
        }

        private bool ShouldShowTutorial()
        {
            _shouldShowTutorial = false;
            if (_shouldShowTutorial)
            {
                // Make sure the scene doesn't have a non block version of the OVRCameraRig
                _shouldShowTutorial = !BlockData.HasNonBuildingBlockCameraRig();
            }

            return _shouldShowTutorial;
        }

        private void ShowTutorial(Rect dragArea)
        {
            if (_outline == null && OVRGUIContent.BuildPath("bb_outline.asset", OVRGUIContent.Source.BuildingBlocksAnimations, out var outlinePath))

            {
                _outline = AssetDatabase.LoadAssetAtPath<OVRAnimatedContent>(outlinePath);
            }

            if (_outline != null)
            {
                _outline.Update();
                GUI.DrawTexture(dragArea, _outline.CurrentFrame);
            }

            if (_tutorial == null && OVRGUIContent.BuildPath("bb_tutorial.asset", OVRGUIContent.Source.BuildingBlocksAnimations, out var tutorialPath))

            {
                _tutorial = AssetDatabase.LoadAssetAtPath<OVRAnimatedContent>(tutorialPath);
            }

            if (_tutorial != null)
            {
                _tutorial.Update();
                GUI.DrawTexture(dragArea, _tutorial.CurrentFrame);
            }

            Repaint();
        }

        private bool ShowLargeButton(string controlId, OVRGUIContent icon)
        {
            var previousColor = GUI.color;
            GUI.color = Color.white;
            var id = controlId + icon.Name;
            var hit = OVREditorUtils.HoverHelper.Button(id, icon, Styles.LargeButton, out var hover);
            isHoveringHotControl |= hover;
            GUI.color = previousColor;
            EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
            return hit;
        }

        private static void OnAdd(BlockBaseData block)
        {
            block.AddToProject();
        }

#if OVR_BB_DRAGANDDROP
        private void RefreshDragAndDrop(int expectedThumbnailWidth, int expectedThumbnailHeight)
        {
            var blockThumbnail = DragAndDrop.GetGenericData(DragAndDropBlockThumbnailLabel) as Texture2D;
            if (blockThumbnail)
            {
                var cursorOffset = new Vector2(expectedThumbnailWidth / 2.0f, expectedThumbnailHeight / 2.0f);
                var cursorRect = new Rect(Event.current.mousePosition - cursorOffset, new Vector2(expectedThumbnailWidth, expectedThumbnailHeight));
                GUI.color = new Color(1, 1, 1, Styles.DragOpacity);
                GUI.DrawTexture(cursorRect, blockThumbnail, ScaleMode.ScaleAndCrop);
                GUI.color = Color.white;

                // Enforce a repaint next frame, as we need to move this thumbnail everyframe
                Repaint();
            }

            if (Event.current.type == EventType.DragExited)
            {
                ResetDragThumbnail();
            }

            if (Event.current.type == EventType.DragUpdated)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
            }
        }

        private static DragAndDropVisualMode HierarchyDropHandler(
            int dropTargetInstanceID,
            HierarchyDropFlags dropMode,
            Transform parentForDraggedObjects,
            bool perform)
        {
            var hoveredObject = EditorUtility.InstanceIDToObject(dropTargetInstanceID) as GameObject;
            return DropHandler(perform, hoveredObject);
        }

        private static DragAndDropVisualMode SceneDropHandler(
            UnityEngine.Object dropUpon,
            Vector3 worldPosition,
            Vector2 viewportPosition,
            Transform parentForDraggedObjects,
            bool perform)
        {
            return DropHandler(perform, dropUpon as GameObject);
        }

        private static DragAndDropVisualMode DropHandler(bool perform, GameObject dropUpon)
        {
            var block = DragAndDrop.GetGenericData(DragAndDropBlockDataLabel) as BlockBaseData;
            if (block != null)
            {
                if (perform)
                {
                    block.AddToProject(dropUpon);
                    ResetDragAndDrop();
                    _tutorialCompleted.Value = true;
                    _shouldShowTutorial = false;
                }

                return DragAndDropVisualMode.Generic;
            }
            return DragAndDropVisualMode.None;
        }

        private static void SetDragAndDrop(BlockBaseData block)
        {
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.SetGenericData(DragAndDropBlockDataLabel, block);
            DragAndDrop.SetGenericData(DragAndDropBlockThumbnailLabel, block.Thumbnail);
            DragAndDrop.StartDrag(DragAndDropLabel);
        }

        private static void ResetDragThumbnail()
        {
            DragAndDrop.SetGenericData(DragAndDropBlockThumbnailLabel, null);
        }

        private static void ResetDragAndDrop()
        {
            DragAndDrop.SetGenericData(DragAndDropBlockDataLabel, null);
        }
#endif // OVR_BB_DRAGANDDROP
    }
}
