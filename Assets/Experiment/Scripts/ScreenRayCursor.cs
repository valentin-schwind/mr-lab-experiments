using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace ExperimentController
{
    public class ScreenRayCursor : MonoBehaviour
    {
        public enum InputMode
        {
            Mouse,
            Controller
        }

        [Header("Input Mode")]
        public InputMode inputMode = InputMode.Mouse;
        public bool autoSwitchInputMode = true;

        [Header("Cursor Settings")]
        public GameObject cursorObject;
        public float cursorScreenSize = 0.01f;
        public float maxDistance = 100f;
        public float surfaceOffset = 0.001f;

        [Header("Pointer Sources (VR)")]
        public GameObject ovrRightController;
        public GameObject ovrLeftController;

        [Header("Allowed UI Targets")]
        [Tooltip("Explicit list of RectTransforms that should be treated as UI hit surfaces (e.g. questionnaire panels).")]
        public RectTransform[] uiTargets;

        void Start()
        {
            // Create cursor sphere if not assigned
            if (cursorObject == null)
            {
                cursorObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                cursorObject.name = "ScreenRayCursorSphere";

                var collider = cursorObject.GetComponent<Collider>();
                if (collider != null)
                {
                    Destroy(collider);
                }
            }

            SetupCursorAppearance();
            cursorObject.SetActive(false);

            // Auto-assign controller anchors if not set
            if (ovrRightController == null)
            {
                var right = GameObject.Find("RightHandAnchor");
                if (right != null) ovrRightController = right;
            }

            if (ovrLeftController == null)
            {
                var left = GameObject.Find("LeftHandAnchor");
                if (left != null) ovrLeftController = left;
            }
        }

        void SetupCursorAppearance()
        {
            var renderer = cursorObject.GetComponent<Renderer>();
            if (renderer != null)
            {
                Shader shader = Shader.Find("Unlit/Color");
                if (shader == null) shader = Shader.Find("Standard");

                Material mat = new Material(shader);

                if (shader.name == "Unlit/Color")
                {
                    mat.color = Color.red;
                }
                else
                {
                    mat.color = Color.red;
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", Color.red);
                }

                renderer.material = mat;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        void Update()
        {
            if (autoSwitchInputMode)
            {
                UpdateInputModeFromLastInput();
            }

            bool hasHit = false;
            Vector3 hitPoint = Vector3.zero;
            Vector3 hitNormal = Vector3.up;

            if (inputMode == InputMode.Mouse)
            {
                if (Mouse.current != null && Camera.main != null)
                {
                    Vector2 mousePos = Mouse.current.position.ReadValue();
                    Ray ray = Camera.main.ScreenPointToRay(mousePos);

                    Debug.DrawRay(ray.origin, ray.direction * maxDistance, Color.green);

                    if (GetHitFromRay(ray, out hitPoint, out hitNormal))
                    {
                        hasHit = true;
                    }
                }
            }
            else if (inputMode == InputMode.Controller)
            {
                if (ovrRightController != null)
                {
                    Ray rayRight = new Ray(ovrRightController.transform.position, ovrRightController.transform.forward);
                    Debug.DrawRay(rayRight.origin, rayRight.direction * maxDistance, Color.red);

                    if (GetHitFromRay(rayRight, out hitPoint, out hitNormal))
                    {
                        hasHit = true;
                    }
                }

                if (!hasHit && ovrLeftController != null)
                {
                    Ray rayLeft = new Ray(ovrLeftController.transform.position, ovrLeftController.transform.forward);
                    Debug.DrawRay(rayLeft.origin, rayLeft.direction * maxDistance, Color.blue);

                    if (GetHitFromRay(rayLeft, out hitPoint, out hitNormal))
                    {
                        hasHit = true;
                    }
                }
            }

            if (hasHit)
            {
                UpdateCursor(hitPoint, hitNormal);
            }
            else if (cursorObject.activeSelf)
            {
                cursorObject.SetActive(false);
            }
        }

        void UpdateInputModeFromLastInput()
        {
            // Last mouse click -> Mouse mode
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                inputMode = InputMode.Mouse;
                return;
            }

            // Last VR trigger -> Controller mode
            if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger))
            {
                inputMode = InputMode.Controller;
            }
        }

        bool GetHitFromRay(Ray ray, out Vector3 hitPoint, out Vector3 hitNormal)
        {
            hitPoint = Vector3.zero;
            hitNormal = Vector3.up;

            // 1) UI hit on any explicitly assigned RectTransform (only if active)
            if (uiTargets != null && uiTargets.Length > 0)
            {
                if (TryHitUI(ray, out hitPoint, out hitNormal))
                {
                    return true;
                }
            }

            // 2) Fallback: regular 3D physics
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, maxDistance))
            {
                hitPoint = hit.point;
                hitNormal = hit.normal;
                return true;
            }

            return false;
        }

        bool TryHitUI(Ray ray, out Vector3 hitPoint, out Vector3 hitNormal)
        {
            hitPoint = Vector3.zero;
            hitNormal = Vector3.up;

            bool found = false;
            float bestEnter = float.MaxValue;

            foreach (var rt in uiTargets)
            {
                if (rt == null) continue;
                if (!rt.gameObject.activeInHierarchy) continue; // only hit active UIs

                if (TryHitRectTransform(ray, rt, out Vector3 p, out Vector3 n, out float enter))
                {
                    if (enter < bestEnter)
                    {
                        bestEnter = enter;
                        hitPoint = p;
                        hitNormal = n;
                        found = true;
                    }
                }
            }

            return found;
        }

        bool TryHitRectTransform(Ray ray, RectTransform rectTransform, out Vector3 hitPoint, out Vector3 hitNormal, out float enter)
        {
            hitPoint = Vector3.zero;
            hitNormal = Vector3.up;
            enter = 0f;

            Plane plane = new Plane(rectTransform.forward, rectTransform.position);

            if (!plane.Raycast(ray, out enter)) return false;
            if (enter < 0f || enter > maxDistance) return false;

            Vector3 worldHit = ray.GetPoint(enter);
            Vector3 local = rectTransform.InverseTransformPoint(worldHit);
            Rect rect = rectTransform.rect;

            if (!rect.Contains(new Vector2(local.x, local.y))) return false;

            hitPoint = worldHit;
            hitNormal = rectTransform.forward;
            return true;
        }

        void UpdateCursor(Vector3 worldPoint, Vector3 normal)
        {
            Vector3 pos = worldPoint + normal * surfaceOffset;
            cursorObject.transform.position = pos;

            Camera cam = Camera.main;
            if (cam != null)
            {
                float dist = Vector3.Distance(cam.transform.position, pos);
                float scale = cursorScreenSize * dist;
                cursorObject.transform.localScale = Vector3.one * scale;
            }

            if (!cursorObject.activeSelf) cursorObject.SetActive(true);
        }
    }
}
