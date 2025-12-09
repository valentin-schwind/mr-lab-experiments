using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace ExperimentController {
    public class QuestionnaireCursor : MonoBehaviour {
        public enum InputMode {
            MouseOnly,
            ControllersOnly
        }

        [Header("Eingabemodus")]
        public InputMode inputMode = InputMode.MouseOnly;
        public bool autoSwitchInputMode = true; // automatisch zwischen Maus/Controller umschalten

        [Header("Cursor Einstellungen")]
        public GameObject cursorObject;          // Wird zur Laufzeit erzeugt, wenn leer
        public float cursorScreenSize = 0.01f;   // "Größe" aus Kamerasicht
        public float maxDistance = 100f;         // Raycast-Reichweite
        public float surfaceOffset = 0.001f;     // Leicht vor der Oberfläche

        [Header("Pointer Quellen (VR)")]
        public GameObject ovrRightController;    // RightHandAnchor
        public GameObject ovrLeftController;     // LeftHandAnchor (optional)

        [Header("Questionnaire-Fläche")]
        public RectTransform questionnaireRect;  // World-Space-Canvas oder Panel mit QT-GUI

        void Start() {
            // Cursor-Kugel erzeugen, falls nicht gesetzt
            if (cursorObject == null) {
                cursorObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                cursorObject.name = "QuestionnaireCursorSphere";

                var collider = cursorObject.GetComponent<Collider>();
                if (collider != null) {
                    Destroy(collider);
                }
            }

            // Material / Rendering einstellen: leuchtend rot, unlit, keine Schatten
            SetupCursorAppearance();

            cursorObject.SetActive(false);

            // Optional: Controller automatisch finden, wenn nicht im Inspector gesetzt
            if (ovrRightController == null) {
                var right = GameObject.Find("RightHandAnchor");
                if (right != null) {
                    ovrRightController = right;
                }
            }

            if (ovrLeftController == null) {
                var left = GameObject.Find("LeftHandAnchor");
                if (left != null) {
                    ovrLeftController = left;
                }
            }
        }

        void SetupCursorAppearance() {
            var renderer = cursorObject.GetComponent<Renderer>();
            if (renderer != null) {
                // Versuche einen unlit Shader, sonst fallback auf Standard
                Shader shader = Shader.Find("Unlit/Color");
                if (shader == null) {
                    shader = Shader.Find("Standard");
                }

                Material mat = new Material(shader);

                if (shader.name == "Unlit/Color") {
                    mat.color = Color.red;
                } else {
                    // Standard Shader: rote, emissive Kugel
                    mat.color = Color.red;
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", Color.red);
                }

                renderer.material = mat;

                // Keine Schatten
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        void Update() {
            if (autoSwitchInputMode) {
                UpdateInputModeFromLastInput();
            }

            bool hasHit = false;
            Vector3 hitPoint = Vector3.zero;
            Vector3 hitNormal = Vector3.up;

            if (inputMode == InputMode.MouseOnly) {
                // Nur Maus
                if (Mouse.current != null && Camera.main != null) {
                    Vector2 mousePos = Mouse.current.position.ReadValue();
                    Ray mouseRay = Camera.main.ScreenPointToRay(mousePos);

                    Debug.DrawRay(mouseRay.origin, mouseRay.direction * maxDistance, Color.green);

                    if (GetHitFromRay(mouseRay, "Mouse", out hitPoint, out hitNormal)) {
                        hasHit = true;
                    }
                }
            } else if (inputMode == InputMode.ControllersOnly) {
                // Nur Controller
                // 1) Rechter Controller
                if (ovrRightController != null) {
                    Ray rayRight = new Ray(ovrRightController.transform.position, ovrRightController.transform.forward);

                    Debug.DrawRay(rayRight.origin, rayRight.direction * maxDistance, Color.red);

                    if (GetHitFromRay(rayRight, "RightController", out hitPoint, out hitNormal)) {
                        hasHit = true;
                    }
                }

                // 2) Wenn rechter nichts trifft, linker Controller
                if (!hasHit && ovrLeftController != null) {
                    Ray rayLeft = new Ray(ovrLeftController.transform.position, ovrLeftController.transform.forward);

                    Debug.DrawRay(rayLeft.origin, rayLeft.direction * maxDistance, Color.blue);

                    if (GetHitFromRay(rayLeft, "LeftController", out hitPoint, out hitNormal)) {
                        hasHit = true;
                    }
                }
            }

            if (hasHit) {
                UpdateCursor(hitPoint, hitNormal);
            } else {
                if (cursorObject != null && cursorObject.activeSelf) {
                    cursorObject.SetActive(false);
                }
            }
        }

        // Automatische Umschaltung je nach letzter Eingabe
        void UpdateInputModeFromLastInput() {
            // Letzter Mausklick -> Mausmodus
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) {
                inputMode = InputMode.MouseOnly;
                return;
            }

            // Letzter VR-Trigger -> Controllermodus
            // (OVRInput kommt aus dem Oculus Integration Package)
            if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger)) {
                inputMode = InputMode.ControllersOnly;
            }
        }

        private bool GetHitFromRay(Ray ray, string sourceLabel, out Vector3 hitPoint, out Vector3 hitNormal) {
            hitPoint = Vector3.zero;
            hitNormal = Vector3.up;

            // 1) Versuch: Ebene des World-Space-Questionnaire-Rects schneiden
            if (questionnaireRect != null) {
                if (TryHitQuestionnairePlane(ray, out hitPoint, out hitNormal)) {
                    return true;
                }
            }

            // 2) Fallback: normaler Physics.Raycast (3D-Objekte)
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, maxDistance)) {
                hitPoint = hit.point;
                hitNormal = hit.normal;
                return true;
            }

            return false;
        }

        private bool TryHitQuestionnairePlane(Ray ray, out Vector3 hitPoint, out Vector3 normal) {
            hitPoint = Vector3.zero;
            normal = Vector3.up;

            Plane plane = new Plane(questionnaireRect.forward, questionnaireRect.position);

            float enter;
            if (!plane.Raycast(ray, out enter)) {
                return false;
            }

            if (enter < 0f || enter > maxDistance) {
                return false;
            }

            Vector3 worldHit = ray.GetPoint(enter);

            // In lokale Koordinaten des RectTransforms umrechnen
            Vector3 local = questionnaireRect.InverseTransformPoint(worldHit);
            Rect rect = questionnaireRect.rect;

            // Prüfen, ob der Punkt innerhalb des sichtbaren Rects liegt
            if (!rect.Contains(new Vector2(local.x, local.y))) {
                return false;
            }

            hitPoint = worldHit;
            normal = plane.normal;
            return true;
        }

        private void UpdateCursor(Vector3 worldPoint, Vector3 normal) {
            if (cursorObject == null) {
                return;
            }

            // Position leicht vor der Oberfläche
            Vector3 pos = worldPoint + normal * surfaceOffset;
            cursorObject.transform.position = pos;

            // Aus Kamerasicht immer gleich groß: Scale ∝ Distanz zur Kamera
            Camera cam = Camera.main;
            if (cam != null) {
                float dist = Vector3.Distance(cam.transform.position, pos);
                float scale = cursorScreenSize * dist;
                cursorObject.transform.localScale = Vector3.one * scale;
            }

            if (!cursorObject.activeSelf) {
                cursorObject.SetActive(true);
            }
        }
    }
}
