using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

namespace ExperimentController {
    public class YawRotationTaskController : MonoBehaviour, ITaskController { 

        public enum TrialOrderMode {
            InOrder,                  // no shuffling
            ShuffleAllAnglesByHash,   // shuffle distinct angles, then repeat in that order
            ShuffleAllTrialsByHash // create all trials (with repetitions) and shuffle globally
        }

        [Header("Task Setup")]
        [Tooltip("Transform of an object that will be rotated.")]
        public Transform targetObjectTransform;

        [Tooltip("Transform whose Y-axis direction is interpreted as the participant's response (e.g., RightHandAnchor or CenterEyeAnchor).")]
        public Transform pointingTransform;

        [Header("Trial Configuration")]
        [Tooltip("Number of distinct angles that divide the full 360 degrees.")]
        public int numberOfAngles = 16;

        [Tooltip("Number of repetitions per angle.")]
        public int repetitionsPerAngle = 1;

        [Tooltip("Defines how the trial sequence is ordered and shuffled.")]
        public TrialOrderMode trialOrderMode = TrialOrderMode.ShuffleAllTrialsByHash;

        [Tooltip("Delay between two trials in seconds.")]
        public float interTrialInterval = 0.5f;

        [Header("Task Description UI")]
        [TextArea]
        [Tooltip("Description of the task shown to the participant before or during the trials.")]
        public string taskDescription;

        [Tooltip("Optional TMP text object used to display the task description.")]
        public TextMeshProUGUI taskDescriptionTMP;

        [Tooltip("Canvas that contains the task UI elements (e.g., task description text).")]
        public GameObject taskCanvas;


        // Reference to ExperimentController
        private ExperimentController experimentController;

        // Trial data
        private float[] trialAngles;          // all trial angles (expanded + shuffled)
        private int currentTrialIndex = -1;   // -1 before start, 0..N-1 during trials

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

            // Show UI canvas (if assigned)
            if (taskCanvas != null) {
                taskCanvas.SetActive(true);
            }

            // Show / update task description text (if assigned)
            if (taskDescriptionTMP != null) {
                taskDescriptionTMP.SetText(taskDescription);
            }

            // Ensure target object is visible at the start of a new condition
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

        public void RegisterResponse() {
            if (!conditionActive || conditionComplete || !waitingForResponse) {
                return;
            }

            OnResponse();
        }

        public void hide() {
            gameObject.SetActive(false);
        }

        public void show() {
            gameObject.SetActive(true);
        }

        // ---------------------------------------------------------
        // Unity
        // ---------------------------------------------------------
        void Update() {
            // No input polling here – input is handled centrally in ExperimentController.
        }

        // ---------------------------------------------------------
        // Condition end (centralized)
        // ---------------------------------------------------------
        private void EndCondition() {
            Debug.Log("[YawRotationTask] EndCondition called.");

            conditionComplete = true;
            conditionActive = false;
            waitingForResponse = false;

            // Hide target object
            if (targetObjectTransform != null) {
                targetObjectTransform.gameObject.SetActive(false);
            }

            // Hide task UI
            if (taskCanvas != null) {
                taskCanvas.SetActive(false);
            }

            // Optional: hide TMP explicitly, falls nicht im Canvas
            if (taskDescriptionTMP != null && taskDescriptionTMP.gameObject != taskCanvas) {
                taskDescriptionTMP.gameObject.SetActive(false);
            }
        }

        // ---------------------------------------------------------
        // Trial Setup
        // ---------------------------------------------------------
        private void InitTrials() {
            // Safety clamps
            if (numberOfAngles < 1) {
                numberOfAngles = 1;
            }

            if (repetitionsPerAngle < 1) {
                repetitionsPerAngle = 1;
            }

            // Base angles: 0..360 in equal steps
            float step = 360f / numberOfAngles;
            float[] baseAngles = new float[numberOfAngles];

            for (int i = 0; i < numberOfAngles; i++) {
                baseAngles[i] = i * step;
            }

            int subjectId = experimentController != null ? experimentController.GetSubjectID() : 0;
            int conditionId = experimentController != null ? experimentController.GetCurrentCondition() : 0;
            int subjectConditionHash = System.HashCode.Combine(subjectId, conditionId);

            // Build trialAngles according to order mode and repetitions
            switch (trialOrderMode) {
                case TrialOrderMode.InOrder:
                    trialAngles = BuildTrialsInOrder(baseAngles);
                    break;

                case TrialOrderMode.ShuffleAllAnglesByHash:
                    trialAngles = BuildTrialsShuffleAngles(baseAngles, subjectConditionHash);
                    break;

                case TrialOrderMode.ShuffleAllTrialsByHash:
                    trialAngles = BuildTrialsShuffleAll(baseAngles, subjectConditionHash);
                    break;
            }

            Debug.Log("[YawRotationTask] Trials initialized with SubjectID " + subjectId +
                      " | numberOfAngles=" + numberOfAngles +
                      " | repetitionsPerAngle=" + repetitionsPerAngle +
                      " | totalTrials=" + trialAngles.Length +
                      " | mode=" + trialOrderMode +
                      " | order: " + string.Join(", ", trialAngles));
        }

        private float[] BuildTrialsInOrder(float[] baseAngles) {
            int totalTrials = baseAngles.Length * repetitionsPerAngle;
            float[] trials = new float[totalTrials];

            int idx = 0;
            for (int r = 0; r < repetitionsPerAngle; r++) {
                for (int i = 0; i < baseAngles.Length; i++) {
                    trials[idx++] = baseAngles[i];
                }
            }

            return trials;
        }

        private float[] BuildTrialsShuffleAngles(float[] baseAngles, int subjectId) {
            // Shuffle the distinct angles once by subject
            float[] shuffledAngles = ExperimentController.Shuffle((float[])baseAngles.Clone(), subjectId);

            int totalTrials = shuffledAngles.Length * repetitionsPerAngle;
            float[] trials = new float[totalTrials];

            int idx = 0;
            for (int r = 0; r < repetitionsPerAngle; r++) {
                for (int i = 0; i < shuffledAngles.Length; i++) {
                    trials[idx++] = shuffledAngles[i];
                }
            }

            return trials;
        }

        private float[] BuildTrialsShuffleAll(float[] baseAngles, int subjectId) {
            // First create the full expanded list in deterministic order
            int totalTrials = baseAngles.Length * repetitionsPerAngle;
            float[] trials = new float[totalTrials];

            int idx = 0;
            for (int r = 0; r < repetitionsPerAngle; r++) {
                for (int i = 0; i < baseAngles.Length; i++) {
                    trials[idx++] = baseAngles[i];
                }
            }

            // Then shuffle all trials using the subject seed
            trials = ExperimentController.Shuffle(trials, subjectId);
            return trials;
        }

        private void StartNextTrial() {
            currentTrialIndex++;

            if (currentTrialIndex >= trialAngles.Length) {
                EndCondition();
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

        // 1) Trial index (0-based as string)
        public string GetCurrentTrialIndex() {
            return currentTrialIndex.ToString();
        }

        // 2) Trial name – based on angle label
        public string GetCurrentTrialName() {
            return "Yaw_" + lastTargetAngle.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        }

        // 3) Participant pointing direction (Y-axis)
        public string GetCurrentPointingDirectionY() {
            return lastParticipantDirection.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        // 4) Angular deviation (signed degrees)
        public string GetCurrentAngularError() {
            return lastAngularError.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        // 5) Reaction time in seconds since trial start
        public string GetCurrentReactionTime() {
            return lastReactionTime.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
