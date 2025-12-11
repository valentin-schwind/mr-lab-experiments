using UnityEngine;
using UnityEngine.InputSystem;

namespace ExperimentController {
    public class YawRotationTaskController : MonoBehaviour, ITaskController {

        [Header("Task Setup")]
        [Tooltip("Transform of an object that will be rotated.")]
        public Transform targetObjectTransform;

        [Tooltip("Transform whose Y-axis direction is interpreted as the participant's response (e.g., RightHandAnchor or CenterEyeAnchor).")]
        public Transform pointingTransform;

        [Tooltip("OVR button used by the participant to confirm their response.")]
        public OVRInput.Button responseButton = OVRInput.Button.PrimaryIndexTrigger;

        [Tooltip("If enabled, a left mouse click can also trigger a response.")]
        public bool allowMouseInput = false;

        [Tooltip("Delay between two trials in seconds.")]
        public float interTrialInterval = 0.5f;



        // Reference to ExperimentController
        private ExperimentController experimentController;

        // Trial data
        private float[] trialAngles;          // 16 angles (22.5-degree steps), shuffled by SubjectID
        private int currentTrialIndex = -1;   // -1 before start, 0..15 during trials

        // State
        private bool waitingForResponse = false;
        private bool conditionComplete = false;
        private bool conditionActive = false;

        private float trialStartTime = 0f;

        // Last measured values for logging
        private float lastTargetAngle = float.NaN;
        private float lastParticipantDirection = float.NaN;
        private float lastAngularError = float.NaN;
        private float lastReactionTime = float.NaN;

        // ---------------------------------------------------------
        // ITaskController
        // ---------------------------------------------------------
        public void BeginCondition(ExperimentController controller) {
            experimentController = controller;

            Debug.Log("[YawRotationTask] BeginCondition called.");

            // Ensure mast is visible at the start of a new condition
            if (targetObjectTransform != null) {
                targetObjectTransform.gameObject.SetActive(true);
            }

            InitTrials();
            conditionComplete = false;
            conditionActive = true;
            waitingForResponse = false;
            currentTrialIndex = -1;

            StartNextTrial();
        }


        public bool IsConditionComplete() {
            return conditionComplete;
        }

        // ---------------------------------------------------------
        // Unity
        // ---------------------------------------------------------
        void Update() {
            // No input polling here – input is handled centrally in ExperimentController.
        }

        public void RegisterResponse() {
            if (!conditionActive || conditionComplete || !waitingForResponse) {
                return;
            }

            OnResponse();
        }


        // ---------------------------------------------------------
        // Trial Setup
        // ---------------------------------------------------------
        private void InitTrials() {
            // 16 angles: 0..360 in 22.5-degree increments
            trialAngles = new float[16];
            for (int i = 0; i < 16; i++) {
                trialAngles[i] = i * 22.5f;
            }

            int subjectId = experimentController != null ? experimentController.GetSubjectID() : 0;

            // Shuffle using SubjectID as random seed
            trialAngles = ExperimentController.Shuffle(trialAngles, subjectId);

            Debug.Log("[YawRotationTask] Trials initialized with SubjectID " + subjectId +
                      ". Order: " + string.Join(", ", trialAngles));
        }

        private void StartNextTrial() {
            currentTrialIndex++;

            if (currentTrialIndex >= trialAngles.Length) {
                Debug.Log("[YawRotationTask] All trials completed.");
                conditionComplete = true;
                waitingForResponse = false;
                conditionActive = false;

                // Hide mast after all trials are finished
                if (targetObjectTransform != null) {
                    targetObjectTransform.gameObject.SetActive(false);
                }

                return;
            }

            float angle = trialAngles[currentTrialIndex];

            if (targetObjectTransform != null) {
                targetObjectTransform.localRotation = Quaternion.Euler(0f, angle, 0f);
            }

            lastTargetAngle = angle;
            trialStartTime = Time.time;
            waitingForResponse = true;

            Debug.Log("[YawRotationTask] Trial " + currentTrialIndex +
                      " started. Target angle (Y): " + angle + " degrees.");
        }


        // ---------------------------------------------------------
        // Response Handling
        // ---------------------------------------------------------
        private void OnResponse() {
            waitingForResponse = false;

            if (pointingTransform == null) {
                Debug.LogWarning("[YawRotationTask] pointingTransform is not assigned! Cannot measure direction.");
                lastParticipantDirection = float.NaN;
                lastAngularError = float.NaN;
            } else {
                lastParticipantDirection = pointingTransform.eulerAngles.y;
                float signedError = Mathf.DeltaAngle(lastTargetAngle, lastParticipantDirection);
                lastAngularError = signedError;
            }

            lastReactionTime = Time.time - trialStartTime;

            Debug.Log("[YawRotationTask] Response received. Trial " + currentTrialIndex +
                      " | Target angle: " + lastTargetAngle +
                      " | Participant Y: " + lastParticipantDirection +
                      " | Error (signed degrees): " + lastAngularError +
                      " | Reaction time (s): " + lastReactionTime);

            if (!conditionComplete) {
                Invoke(nameof(StartNextTrial), interTrialInterval);
            }
        }

        // ---------------------------------------------------------
        // Public Getters for ExperimentController / Logging
        // ---------------------------------------------------------
         
        public string GetCurrentTrialIndex() {
            return currentTrialIndex.ToString();
        } 

        public string GetCurrentTrialName() {
            return "Yaw_" + lastTargetAngle.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        }
         
        public string GetCurrentPointingDirectionY() {
            return lastParticipantDirection.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
         
        public string GetCurrentAngularError() {
            return lastAngularError.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
         
        public string GetCurrentReactionTime() {
            return lastReactionTime.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public void hide() {
            this.gameObject.SetActive(false);
        }

        public void show() {
            this.gameObject.SetActive(true);
        }
    }
}
