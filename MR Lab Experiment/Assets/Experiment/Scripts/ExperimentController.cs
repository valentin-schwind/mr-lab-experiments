using QuestionnaireToolkit.Scripts;  
using System.Collections.Generic;
using System.IO;
using System.Linq; 
using UnityEditor;
using UnityEngine;  
using QuestionnaireToolkit.Scripts.MemberReflection.Reflection; 
using TMPro;

namespace ExperimentController {
    public class ExperimentController : MonoBehaviour {
        public enum SequenceOptions { BalancedLatinSquare, LatinSquare, Permutations, ShuffleBySeed, Shuffle };
        public enum GenderOptions { Male, Female, Other, DoNotWantToSpecify };
        public enum RecruitmentOption { Student, Staff, FamilyAndFriends, InvitedExternal, Other };

        private SequenceOptions selectedOrder;

        // Enter them before running an experiment
        [Header("Demographics")]
        public int SubjectID;
        public int Age;
        public GenderOptions Gender;
        public RecruitmentOption Recruitment;

        // Options for fixed timings
        [Header("Study Timing")]
        [TextArea]
        public string instructionText;
        public string countdownText;
        public TextMeshProUGUI countdownTMP; 
        public bool useConditionTimer = true;
        public float countdownTimer = 3;
        public float conditionTimer = 10.0f;
        [ReadOnly] public float conditionTime = 0.0f;
        [ReadOnly] public float experimentTime = 0.0f;
        public int avgFrameRate; 

        // Study Design Options
        [Header("Study Design")]
        public string experimentName; 
        public GameObject[] conditions; // Click on "Conditions" in Inspector, enter # conditions, assign game objects
        public SequenceOptions ConditionSequence;

        // For manual entering
        [Header("Conditions Manual Override")]
        public int currentCondition = 0; // you can manually enter the next condition sequence
        [ReadOnly] public string currentConditionName = ""; // just for displaying the current condition   

        // For our questionnaires
        [Header("Questionnaire Toolkit")]
        public GameObject QTQuestionnaireManager;
        private QTManager qtManager;
        private QTQuestionnaireManager qtQManager;
        private int currentQuestionnaireNumber = 0;

        private bool trialRunning = false;
        private bool countdownRunning = false;
        private bool experimentRunning = false;

        // Logging stuff
        [Header("Logging Paths")]
        [ReadOnly] public string applicationPath = ""; // folder during runtime (cannot user local folder anymore)
        public string loggingPath = @"Results/Logs/";
        public string questionnairePath = @"Results/Questionnaires/";

        // Custom CSV Item
        public bool saveEventLog = true;
        private StreamWriter eventLogFileWriter;

        [Header("Custom CSV Event Log Items")]
        [Reorderable]
        public ReorderableChildList eventLogItems;
        [System.Serializable]
        public class AdditionalCsvItem {
            public string headerName = "Additional Item";
            [Filter(Fields = true, Properties = true, Methods = true)]
            public UnityMember itemValue;
        }
        [System.Serializable]
        public class ReorderableChildList : ReorderableArray<AdditionalCsvItem> { }

        public bool saveFrameLog = true;
        private StreamWriter frameLogFileWriter;

        [Header("Custom CSV Frame Log Items")]
        [Reorderable]
        public ReorderableChildList frameLogItems;

        void Start() {

            hideConditions();

            // Setup scene
            countdownTMP.SetText(instructionText);
             
            if (QTQuestionnaireManager) qtManager = QTQuestionnaireManager.GetComponent<QTManager>();
            else print("No questionnaires loaded.");

            // I hate using user-specific local folders, but we need a persistent directory
            applicationPath = Application.persistentDataPath;
            var systemPath = applicationPath.TrimEnd('/') + "/" + loggingPath;

            print(systemPath);

            if (!Directory.Exists(systemPath)) {
                Directory.CreateDirectory(systemPath);
            }

            if (saveEventLog) {
                // Creates CSV header of the event log file to log what has been hit
                eventLogFileWriter = new StreamWriter(systemPath.TrimEnd('/') + "/" + "eventLog_" + System.DateTime.Now.Ticks + ".csv");
                eventLogFileWriter.AutoFlush = true;
                string headerLine = "TimeStamp,ExperimentName,SubjectID,Gender,Age,Recruitment,ConditionName,ConditionSequence,ConditionTime,ExperimentTime,Source,Target";

                // Add custom CSV event log items
                foreach (var eventItem in eventLogItems) {
                    headerLine += "," + eventItem.headerName;
                }
                eventLogFileWriter.WriteLine(headerLine);
            }

            if (saveFrameLog) {
                // Creates CSV header of the frame log file to log what has been hit
                frameLogFileWriter = new StreamWriter(systemPath.TrimEnd('/') + "/" + "frameLog_" + System.DateTime.Now.Ticks + ".csv");
                frameLogFileWriter.AutoFlush = true;
                string headerLine = "TimeStamp,ExperimentName,SubjectID,Gender,Age,Recruitment,ConditionName,ConditionSequence,ConditionTime,ExperimentTime,SourceX,SourceY,SourceZ,TargetX,TargetY,TargetZ";

                // Add custom CSV frame log items
                foreach (var frameItem in frameLogItems) {
                    headerLine += "," + frameItem.headerName;
                    frameLogFileWriter.WriteLine(headerLine);
                }
            } 

            // Shuffles the conditions sequence to prevent carry-over effects
            switch (selectedOrder) {
                // Balanced latin square
                case SequenceOptions.BalancedLatinSquare:
                    conditions = GetBalancedLatinSquare(conditions, SubjectID);
                    break;

                // Simple latin square
                case SequenceOptions.LatinSquare:
                    conditions = GetLatinSquare(conditions, SubjectID);
                    break;

                // Go through all permutations
                case SequenceOptions.Permutations:
                    conditions = GetAllPermutations(conditions, SubjectID);
                    break;

                // Psuedo andom array shuffler with SubjectID as seed
                case SequenceOptions.ShuffleBySeed:
                    conditions = Shuffle(conditions, SubjectID);
                    break;

                // Fully random array shuffler
                case SequenceOptions.Shuffle:
                    conditions = Shuffle(conditions);
                    break;
            } 
        } 

        // random method to show how custom CSV items work in inspector
        public string getFPS() {
            // determine FPS  
            return (1f / Time.unscaledDeltaTime).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        void Update() {

            // we can go forward
            if (Input.GetKeyDown(KeyCode.Space)) {
                startCountdown();
                print("space pressed");
            }

            if (experimentRunning) {
                experimentTime += Time.deltaTime;

                // we can go forward
                if (Input.GetKeyDown(KeyCode.RightArrow)) {
                    nextCondition();
                }

                // we can go back
                if (Input.GetKeyDown(KeyCode.LeftArrow)) {
                    previousCondition();
                }

                // Stops logging and trial
                if (trialRunning) {
                    // if timer is out, show questionnaire or proceed
                    conditionTime += Time.deltaTime;
                    if ((conditionTime > conditionTimer) && useConditionTimer) {
                        print("Condition stopped.");
                        if (QTQuestionnaireManager) showQuestionnaire();
                        else nextCondition();
                    }

                    // The experimenter can press the right arrow key to continue
                    //if (Input.GetKeyDown(KeyCode.Space)) {
                    //    showQuestionnaire();
                    //}

                    // Random event logging for mouse or controller hit with condition 
                    // log if mouse hit object
                    if (saveEventLog) {
                        if (Input.GetMouseButtonDown(0)) {
                            RaycastHit raycastHit;
                            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

                            //log when the mouse hits something
                            if (Physics.Raycast(ray, out raycastHit, 100f)) {
                                if (raycastHit.transform != null) {
                                    logEvent(Camera.main.gameObject, raycastHit.transform.gameObject);
                                }
                            }
                        }

                        // log if any VR controller hits object
                        if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger)) {
                            RaycastHit raycastHit;
                            GameObject ovrRightController = GameObject.Find("RightHandAnchor");
                            GameObject ovrLeftController = GameObject.Find("LeftHandAnchor");
                            Ray rayRight = new Ray(ovrRightController.transform.position, ovrRightController.transform.forward);
                            Ray rayLeft = new Ray(ovrLeftController.transform.position, ovrLeftController.transform.forward);

                            //log when the right controller hits something
                            if (Physics.Raycast(rayRight, out raycastHit, 100f)) {
                                if (raycastHit.transform != null) {
                                    logEvent(ovrRightController, raycastHit.transform.gameObject);
                                }
                            }

                            //log when the left controller hits something
                            if (Physics.Raycast(rayLeft, out raycastHit, 100f)) {
                                if (raycastHit.transform != null) {
                                    logEvent(ovrLeftController, raycastHit.transform.gameObject);
                                }
                            }
                        }
                    }
                    if (saveFrameLog) {
                        //logFrame(ovrLeftController, raycastHit.transform.gameObject);
                    }
                }
            } else {
                // start countdown if experiment is not running
                if(!experimentRunning && countdownRunning) { 
                    countdownTimer -= Time.deltaTime;

                    int roundCountDownTimer = Mathf.RoundToInt(countdownTimer);
                    countdownTMP.SetText(countdownText + " " + (roundCountDownTimer));
                    if (roundCountDownTimer == 0) { 
                        countdownRunning = false;
                        countdownTMP.transform.parent.gameObject.SetActive(false);
                        showCondition(currentCondition);
                    }
                } 
            }
        }

        private void startCountdown() { 
            if (!experimentRunning && !countdownRunning) {
                countdownRunning = true;
            }
        }

        public string getCurrentConditionName() {
            return currentConditionName;
        }

        public int getCurrentCondition() {
            return currentCondition;
        }

        public int getSubjectID() {
            return SubjectID;
        }

        public int getAge() {
            return Age;
        }

        public string getGender() {
            return Gender.ToString();
        }
        public string getRecruitment() {
            return Recruitment.ToString();
        }

        // Custom function to render a questionnaire
        public void nextQuestionnaire() {
            if (QTQuestionnaireManager) {
                print("Showing next questionnaire...");
                qtQManager.HideQuestionnaire();
                currentQuestionnaireNumber++;
                qtQManager = qtManager.questionnaires[currentQuestionnaireNumber];
                qtQManager.resultsSavePath = "/" + questionnairePath.TrimStart('/').TrimEnd('/') + "/";
                qtQManager.StartQuestionnaire();
            } 
        }

        // We can go back
        private void previousCondition() {
            if (currentCondition > 0) {
                currentCondition--;
                showCondition(currentCondition);
            }
        }

        // Show the conditions cased on the sequence ID
        private void showCondition(int sequenceID) {
            // Starts running a condition
            conditionTime = 0;
            experimentRunning = true;
            trialRunning = true;

            if (currentCondition > conditions.Length) {
                print("Current condition higher than maximum number of conditions");
                return;
            }
            sequenceID = currentCondition;

            for (int i = 0; i < conditions.Length; i++) {
                conditions[i].SetActive(i == sequenceID);
            }
            currentConditionName = conditions[sequenceID].name;
            print("Current Subject: " + SubjectID + "Current Condition: " + sequenceID + " Condition Name: " + conditions[sequenceID].name);
        }


        // Hide and stops running a condition
        private void hideConditions() { 
            conditionTime = 0;
            trialRunning = false;

            for (int i = 0; i < conditions.Length; i++) {
                conditions[i].SetActive(false);
            }
        }

        // Stop writing event log file
        private void stopRecording() {
            eventLogFileWriter.Close();
            eventLogFileWriter.Dispose();
            frameLogFileWriter.Close();
            frameLogFileWriter.Dispose();
        }

        // Writes event into the log-file
        private void logEvent(GameObject source, GameObject target) {
            string fileOutput = System.DateTime.Now.Ticks + "," +
                experimentName + "," +
                SubjectID + "," +
                Gender + "," +
                Age + "," +
                Recruitment + "," +
                currentCondition + "," +
                currentConditionName + "," +
                conditionTime.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                experimentTime.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                source.name + "," +
                target.name;

            foreach(var eventItem in eventLogItems) { 
                if (eventItem.itemValue.isAssigned) {
                    fileOutput += "," + eventItem.itemValue.GetOrInvoke();
                } else {
                    fileOutput += ",\"NULL\"";
                }
            }

            print(fileOutput);
            eventLogFileWriter.WriteLine(fileOutput);
        }
        // Writes event into the log-file
        private void logFrame(GameObject source, GameObject target) {
            string fileOutput = System.DateTime.Now.Ticks + "," +
                experimentName + "," +
                SubjectID + "," +
                Gender + "," +
                Age + "," +
                Recruitment + "," +
                currentCondition + "," +
                currentConditionName + "," +
                conditionTime.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                experimentTime.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                source.transform.position.x.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                source.transform.position.y.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                source.transform.position.z.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                target.transform.position.x.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                target.transform.position.y.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                target.transform.position.z.ToString(System.Globalization.CultureInfo.InvariantCulture);

            foreach (var eventItem in frameLogItems) { 
                if (eventItem.itemValue.isAssigned) {
                    fileOutput += "," + eventItem.itemValue.GetOrInvoke();
                } else {
                    fileOutput += ",\"NULL\"";
                }
            }

            print(fileOutput);
            frameLogFileWriter.WriteLine(fileOutput);
        }

        // Show the next condition
        public void nextCondition() {
            if (QTQuestionnaireManager) {
                foreach (var questionnaire in qtManager.questionnaires) {
                    questionnaire.ResetQuestionnaire();
                    questionnaire.HideQuestionnaire();
                }
                currentQuestionnaireNumber = 0;
            }
            if (currentCondition < conditions.Length - 1) {
                currentCondition++;
                showCondition(currentCondition);
            } else {
                stopRecording();
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#elif UNITY_WEBPLAYER
                Application.OpenURL(webplayerQuitURL);
#else
                Application.Quit();
#endif
            }
        }

        // Display a questionnaire and stop running the condition
        private void showQuestionnaire() {
            trialRunning = false;
            hideConditions();
            qtQManager = qtManager.questionnaires[currentQuestionnaireNumber];
            qtQManager.resultsSavePath = "/" + questionnairePath.TrimStart('/').TrimEnd('/') + "/";
            qtQManager.StartQuestionnaire();
        }

        // Latin Square Based on "Bradley, J. V. Complete counterbalancing of immediate sequential effects in a Latin square design. J. Amer. Statist. Ass.,.1958, 53, 525-528. "
        public static T[] GetBalancedLatinSquare<T>(T[] array, int participant) {
            List<T> result = new List<T>();
            for (int i = 0, j = 0, h = 0; i < array.Length; ++i) {
                var val = 0;
                if (i < 2 || i % 2 != 0) {
                    val = j++;
                } else {
                    val = array.Length - h - 1;
                    ++h;
                }

                var idx = (val + participant) % array.Length;
                result.Add(array[idx]);
            }

            if (array.Length % 2 != 0 && participant % 2 != 0) {
                result.Reverse();
            }

            return result.ToArray();
        }

        // Simple Latin Square
        public static T[] GetLatinSquare<T>(T[] array, int participant) {
            // 1. Create initial square.
            int[,] latinSquare = new int[array.Length, array.Length];

            // 2. Initialise first row.
            latinSquare[0, 0] = 1;
            latinSquare[0, 1] = 2;

            for (int i = 2, j = 3, k = 0; i < array.Length; i++) {
                if (i % 2 == 1)
                    latinSquare[0, i] = j++;
                else
                    latinSquare[0, i] = array.Length - (k++);
            }

            // 3. Initialise first column.
            for (int i = 1; i <= array.Length; i++) {
                latinSquare[i - 1, 0] = i;
            }

            // 4. Fill in the rest of the square.
            for (int row = 1; row < array.Length; row++) {
                for (int col = 1; col < array.Length; col++) {
                    latinSquare[row, col] = (latinSquare[row - 1, col] + 1) % array.Length;

                    if (latinSquare[row, col] == 0)
                        latinSquare[row, col] = array.Length;
                }
            }

            T[] newArray = new T[array.Length];

            for (int col = 0; col < array.Length; col++) {
                int row = (participant + 1) % (array.Length);
                newArray[col] = array[latinSquare[row, col] - 1];
            }

            return newArray;
        }

        // Go through all permutations
        public static T[] GetAllPermutations<T>(T[] array, int participant) {
            List<List<T>> results = GeneratePermutations<T>(array.ToList());
            T[] newArray = new T[array.Length];
            int row = (participant + 1) % (results.Count);
            for (int i = 0; i < results[row].Count; i++) {
                newArray[i] = results[row][i];
            }
            return newArray;
        }

        // Generate permutations
        private static List<List<T>> GeneratePermutations<T>(List<T> items) {
            T[] current_permutation = new T[items.Count];
            bool[] in_selection = new bool[items.Count];
            List<List<T>> results = new List<List<T>>();
            PermuteItems<T>(items, in_selection, current_permutation, results, 0);
            return results;
        }

        // Permute items
        private static void PermuteItems<T>(List<T> items, bool[] in_selection, T[] current_permutation, List<List<T>> results, int next_position) {
            if (next_position == items.Count) {
                results.Add(current_permutation.ToList());
            } else {
                for (int i = 0; i < items.Count; i++) {
                    if (!in_selection[i]) {
                        in_selection[i] = true;
                        current_permutation[next_position] = items[i];
                        PermuteItems<T>(items, in_selection, current_permutation, results, next_position + 1);
                        in_selection[i] = false;
                    }
                }
            }
        }

        // Fully random shuffler
        public static T[] Shuffle<T>(T[] array) {
            int n = array.Length;
            for (int i = 0; i < n; i++) {
                int r = i +
                    (int)(Random.Range(0.0f, 1.0f) * (n - i));
                T t = array[r];
                array[r] = array[i];
                array[i] = t;
            }
            return array;
        }

        // Seed depending pseudo-random shuffler
        public static T[] Shuffle<T>(T[] array, int seed) {
            System.Random rand = new System.Random(seed);
            for (int i = array.Length - 1; i > 0; i--) {
                int j = rand.Next(i + 1);
                T temp = array[i];
                array[i] = array[j];
                array[j] = temp;
            }
            return array;
        }


    }


    // Enables Ready Only fields of variables in inspector
    public class ReadOnlyAttribute : PropertyAttribute { }
    public class ShowOnlyAttribute : PropertyAttribute { }

    [CustomPropertyDrawer(typeof(ReadOnlyAttribute))]
    public class ReadOnlyDrawer : PropertyDrawer {
        public override float GetPropertyHeight(SerializedProperty property,
                                                GUIContent label) {
            return EditorGUI.GetPropertyHeight(property, label, true);
        }

        public override void OnGUI(Rect position,
                                   SerializedProperty property,
                                   GUIContent label) {
            GUI.enabled = false;
            EditorGUI.PropertyField(position, property, label, true);
            GUI.enabled = true;
        }
    }
}