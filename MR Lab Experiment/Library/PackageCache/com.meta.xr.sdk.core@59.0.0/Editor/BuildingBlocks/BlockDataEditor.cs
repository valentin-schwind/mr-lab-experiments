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

using System;
using System.Collections.Generic;
using System.Linq;
using Meta.XR.Editor.Tags;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Meta.XR.BuildingBlocks.Editor
{
    /// <summary>
    /// An inspector override that shows BlockData information.
    /// </summary>
    [CustomEditor(typeof(BlockData), true)]
    public class BlockDataEditor : UnityEditor.Editor
    {
        private ReorderableList _dependencyList;
        private bool _foldoutInstruction = false;
        private bool _foldoutAdvanced;

        private void OnEnable()
        {
            var depProperty = serializedObject.FindProperty(nameof(BlockData.dependencies));
            _dependencyList = new ReorderableList(serializedObject, depProperty)
            {
                drawElementCallback = DrawListItems,
                drawHeaderCallback = rect => { EditorGUI.LabelField(rect, "Dependencies", EditorStyles.boldLabel); },
                // default behaviour is to duplicate last element when Add button clicked, overriding as empty
                onAddDropdownCallback = (rect, list) =>
                {
                    list.serializedProperty.arraySize++;
                    list.index = list.serializedProperty.arraySize - 1;
                    list.serializedProperty.GetArrayElementAtIndex(list.index).stringValue = "";
                }
            };

        }


        void DrawListItems(Rect rect, int index, bool isActive, bool isFocused)
        {
            SerializedProperty element = _dependencyList.serializedProperty.GetArrayElementAtIndex(index);
            var blockData = Utils.GetBlockData(element.stringValue);
            var obj = EditorGUI.ObjectField(rect, "", blockData, typeof(BlockData), false) as BlockData;
            if (obj != blockData & obj != null)
            {
                var deps = serializedObject.FindProperty(nameof(BlockData.dependencies));
                deps.GetArrayElementAtIndex(index).stringValue = obj.Id;
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var blockData = (BlockData)serializedObject.targetObject;
            // Thumbnail
            DrawThumbnail(blockData);

            // BlockName
            EditorGUILayout.LabelField(blockData.BlockName, Styles.LabelStyle);

            // Description
            EditorGUILayout.LabelField(blockData.Description, Styles.InfoStyle);

            // Tags
            DrawTags(blockData);

            EditorGUILayout.Space();

            // Usage
            DrawUsageInstructions(blockData);

            // Dependencies
            DrawBlockDataList("Dependencies", blockData.GetAllDependencyDatas());

            serializedObject.ApplyModifiedProperties();

        }


        private void DrawThumbnail(BlockData blockData)
        {
            var currentWidth = EditorGUIUtility.currentViewWidth;
            var expectedHeight = currentWidth / Styles.ThumbnailRatio;
            expectedHeight *= 0.5f;

            // Thumbnail
            var rect = GUILayoutUtility.GetRect(currentWidth, expectedHeight);
            rect.x -= 20;
            rect.width += 40;
            rect.y -= 4;
            GUI.DrawTexture(rect, blockData.Thumbnail, ScaleMode.ScaleAndCrop);

            // Statuses
            GUILayout.BeginArea(new Rect(Styles.TagStyle.margin.left, Styles.TagStyle.margin.top, currentWidth, expectedHeight));
            foreach (var tag in blockData.Tags)
            {
                if (tag.Behavior.ShowOverlay)
                {
                    DrawTag(tag, true);
                }
            }
            GUILayout.EndArea();

            // Separator
            rect = GUILayoutUtility.GetRect(currentWidth, 1);
            rect.x -= 20;
            rect.width += 40;
            rect.y -= 4;
            GUI.DrawTexture(rect, OVREditorUtils.MakeTexture(1, 1, Styles.Colors.AccentColor),
                ScaleMode.ScaleAndCrop);
        }

        private void DrawUsageInstructions(BlockData blockData)
        {
            if (!string.IsNullOrEmpty(blockData.UsageInstructions))
            {
                var rect = EditorGUILayout.BeginVertical();
                var textContent = new GUIContent(blockData.UsageInstructions);
                _foldoutInstruction =
                    EditorGUILayout.Foldout(_foldoutInstruction, "Block instructions", Styles.FoldoutBoldLabel);
                if (_foldoutInstruction)
                {
                    var descStyle = new GUIStyle(EditorStyles.helpBox);
                    descStyle.normal = new GUIStyleState()
                    {
                        textColor = Styles.Colors.LightGray,
                        background = OVREditorUtils.MakeTexture(1, 1, Styles.Colors.DarkGray)
                    };
                    descStyle.fontSize = 11;
                    descStyle.fixedHeight = descStyle.CalcHeight(textContent, rect.width);
                    descStyle.alignment = TextAnchor.UpperLeft;

                    EditorGUILayout.LabelField(blockData.UsageInstructions, descStyle);
                }

                EditorGUILayout.EndVertical();
            }
        }

        private void DrawTags(BlockData blockData)
        {
            var tags = blockData.Tags;
            if (tags.Any())
            {
                var tagStyle = new GUIStyle(EditorStyles.helpBox);
                tagStyle.fontSize = 10;
                tagStyle.stretchHeight = false;
                tagStyle.normal = new GUIStyleState()
                {
                    background = OVREditorUtils.MakeTexture(1, 1, Styles.Colors.DarkBlue),
                    textColor = Styles.Colors.LightGray
                };

                EditorGUILayout.BeginHorizontal();
                foreach (var tag in tags)
                {
                    DrawTag(tag, true);
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawTag(Tag tag, bool overlay = false)
        {
            var tagBehavior = tag.Behavior;
            var style = tagBehavior.Icon != null ? Styles.TagStyleWithIcon : Styles.TagStyle;
            var backgroundColors = overlay ? Styles.TagOverlayBackgroundColors : Styles.TagBackgroundColors;

            var tagContent = new GUIContent(tag.Name);
            var tagSize = style.CalcSize(tagContent);
            var rect = GUILayoutUtility.GetRect(tagContent, style, GUILayout.MinWidth(tagSize.x + 1));
            var color = backgroundColors.GetColor(false, false);
            using (new OVREditorUtils.OVRGUIColorScope(OVREditorUtils.OVRGUIColorScope.Scope.Background, color))
            {
                using (new OVREditorUtils.OVRGUIColorScope(OVREditorUtils.OVRGUIColorScope.Scope.Content,
                           tagBehavior.Color))
                {
                    if (GUI.Button(rect, tagContent, style))
                    {
                    }

                    if (tagBehavior.Icon != null)
                    {
                        GUI.Label(rect, tagBehavior.Icon, Styles.TagIcon);
                    }
                }
            }
        }

        private void DrawBlockDataList(string name, List<BlockData> list)
        {
            EditorGUILayout.LabelField(name, EditorStyles.boldLabel);

            if (list.Count == 0)
            {
                EditorGUILayout.LabelField("No dependency blocks are required.", EditorStyles.helpBox);
            }
            else
            {
                foreach (var dependency in list)
                {
                    DrawBlock(dependency, true);
                }
            }
        }

        private void DrawBlock(BlockData data, bool asGridItem)
        {
            var previousIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            // Thumbnail
            if (asGridItem)
            {
                var gridStyle = Styles.GridItemStyle;
                gridStyle.margin = new RectOffset(0, 0, 0, 0);
                EditorGUILayout.BeginHorizontal(gridStyle);
                EditorGUILayout.BeginHorizontal(Styles.DescriptionAreaStyle);

                var expectedSize = Styles.ItemHeight;
                var rect = GUILayoutUtility.GetRect(0, expectedSize);
                rect.y -= Styles.Padding;
                rect.x -= Styles.Padding;
                rect.width = Styles.ItemHeight;
                GUI.DrawTexture(rect, data.Thumbnail, ScaleMode.ScaleAndCrop);

                EditorGUILayout.Space(Styles.ItemHeight + 2);
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.BeginHorizontal();
            }

            // Label
            EditorGUILayout.BeginVertical();
            var labelStyle = Styles.LabelStyle;
            var labelContent = new GUIContent(data.BlockName);
            EditorGUILayout.LabelField(labelContent, labelStyle, GUILayout.Width(labelStyle.CalcSize(labelContent).x));
            labelStyle = Styles.InfoStyle;
            labelContent = new GUIContent(data.Description);
            EditorGUILayout.LabelField(labelContent, labelStyle, GUILayout.Width(labelStyle.CalcSize(labelContent).x));
            EditorGUILayout.EndVertical();

            GUILayout.FlexibleSpace();

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel = previousIndent;
        }
    }
}
