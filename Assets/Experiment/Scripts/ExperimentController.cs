using QuestionnaireToolkit.Scripts;
using QuestionnaireToolkit.Scripts.MemberReflection.Reflection;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ExperimentController {
    public class ExperimentController : MonoBehaviour {
        public enum SequenceOptions { BalancedLatinSquare, LatinSquare, Permutations, ShuffleBySeed, Shuffle };
        public enum GenderOptions { Male, Female, Other, DoNotWantToSpecify };
        public enum RecruitmentOption { Student, Staff, FamilyAndFriends, InvitedExternal, Other };

        // ---------------------------------------------------------
        // Demographics (werden im Inspector vor dem Experiment gesetzt)
        // ---------------------------------------------------------
        [Header("Demographics")]
        [Tooltip("Eindeutige ID der Versuchsperson (z. B. laufende Nummer).")]
        public int SubjectID;

        [Tooltip("Alter der Versuchsperson in Jahren.")]
        public int Age;

        [Tooltip("Geschlecht der Versuchsperson.")]
        public GenderOptions Gender;

        [Tooltip("Über welchen Kanal wurde die Versuchsperson rekrutiert?")]
        public RecruitmentOption Recruitment;

        // ---------------------------------------------------------
        // Study Timing (Countown, Condition-Zeit etc.)
        // ---------------------------------------------------------
        [Header("Study Timing")]
        [TextArea]
        [Tooltip("Instruktionstext, der vor dem Start des Experiments angezeigt wird.")]
        public string instructionText;

        [Tooltip("Text vor dem Countdown (z. B. 'Das Experiment startet in').")]
        public string countdownText;

        [Tooltip("UI-Text (TMP) für Instruktionen und Countdown.")]
        public TextMeshProUGUI countdownTMP;

        [Tooltip("Wenn aktiv, läuft jede Condition automatisch nach 'conditionTimer' ab.")]
        public bool useConditionTimer = true;

        [Tooltip("Dauer des Countdowns in Sekunden.")]
        public float countdownTimer = 3f;

        [Tooltip("Maximale Dauer pro Condition in Sekunden (nur wenn 'useConditionTimer' aktiv ist).")]
        public float conditionTimer = 10.0f;

        [ReadOnly]
        [Tooltip("Aktuelle Zeit in der laufenden Condition (Sekunden, nur Anzeige).")]
        public float conditionTime = 0.0f;

        [ReadOnly]
        [Tooltip("Gesamte bisher vergangene Experimentzeit (Sekunden, nur Anzeige).")]
        public float experimentTime = 0.0f; 

        // ---------------------------------------------------------
        // Study Design (Bedingungen / Conditions)
        // ---------------------------------------------------------
        [Header("Study Design")]
        [Tooltip("Name des Experiments (taucht im Log auf).")]
        public string experimentName;

        [Tooltip("Alle Conditions (GameObjects), zwischen denen im Experiment umgeschaltet wird.")]
        public GameObject[] conditions;

        [Tooltip("Wie sollen die Conditions für diese Versuchsperson angeordnet werden?")]
        public SequenceOptions ConditionSequence;

        // ---------------------------------------------------------
        // Manuelle Condition-Steuerung
        // ---------------------------------------------------------
        [Header("Conditions Manual Override")]
        [Tooltip("Aktuelle Condition-Index (0-basiert). Kann im laufenden Experiment zu Debug-Zwecken angepasst werden.")]
        public int currentCondition = 0;

        [ReadOnly]
        [Tooltip("Name der aktuell aktiven Condition (nur Anzeige).")]
        public string currentConditionName = "";

        // ---------------------------------------------------------
        // Questionnaire Toolkit
        // ---------------------------------------------------------
        [Header("Questionnaire Toolkit")]
        [Tooltip("GameObject mit QTManager-Komponente (Fragebogen-Toolkit).")]
        public GameObject QTQuestionnaireManager;

        [Tooltip("Canvas, das den Fragebogen enthält (wird zusammen mit dem Fragebogen ein-/ausgeblendet).")]
        public GameObject QTCanvas;

        private QTManager qtManager;
        private QTQuestionnaireManager qtQManager;
        private int currentQuestionnaireNumber = 0;

        // ---------------------------------------------------------
        // interner Ablaufstatus
        // ---------------------------------------------------------
        private bool trialRunning = false;
        private bool countdownRunning = false;
        private bool experimentRunning = false;

        // ---------------------------------------------------------
        // Logging (Dateipfade und Writer)
        // ---------------------------------------------------------
        [Header("Logging Paths")]
        [ReadOnly]
        [Tooltip("Absoluter Pfad zum persistenten Datenverzeichnis der Anwendung.")]
        public string applicationPath = "";

        [Tooltip("Relativer Pfad (vom persistentPath) für Experiment-Logs.")]
        public string loggingPath = @"Results/Logs/";

        [Tooltip("Relativer Pfad (vom persistentPath) für Fragebogen-Logs.")]
        public string questionnairePath = @"Results/Questionnaires/";

        // Event-Log
        [Tooltip("Wenn aktiv, werden einzelne Events (Klicks / Hits) in eine CSV-Datei geschrieben.")]
        public bool saveEventLog = true;
        private StreamWriter eventLogFileWriter;

        [Header("Custom CSV Event Log Items")]
        [Tooltip("Zusätzliche Variablen, die pro Event ins CSV geschrieben werden (z. B. FPS).")]
        [Reorderable]
        public ReorderableChildList eventLogItems;

        // Frame-Log
        [Tooltip("Wenn aktiv, wird pro Frame eine Zeile in eine CSV-Datei geschrieben (z. B. Positionen).")]
        public bool saveFrameLog = true;
        private StreamWriter frameLogFileWriter;

        [Header("Custom CSV Frame Log Items")]
        [Tooltip("Zusätzliche Variablen, die pro Frame ins CSV geschrieben werden.")]
        [Reorderable]
        public ReorderableChildList frameLogItems;

        // ---------------------------------------------------------
        // Cache für VR-Controller
        // ---------------------------------------------------------
        private GameObject ovrRightController;
        private GameObject ovrLeftController;

        // ---------------------------------------------------------
        // Zusatztypen für Custom CSV Items
        // ---------------------------------------------------------
        [System.Serializable]
        public class AdditionalCsvItem {
            [Tooltip("Spaltenname im CSV-File.")]
            public string headerName = "Additional Item";

            [Tooltip("Referenz auf Feld/Eigenschaft/Methode, deren Wert ins CSV geschrieben wird.")]
            [Filter(Fields = true, Properties = true, Methods = true)]
            public UnityMember itemValue;
        }

        [System.Serializable]
        public class ReorderableChildList : ReorderableArray<AdditionalCsvItem> { }

        // ---------------------------------------------------------
        // Unity Lifecycle
        // ---------------------------------------------------------
        void Start() {
            // Zu Beginn alle Conditions deaktivieren
            HideConditions();

            // Instruktionstext im Countdown-Textfeld anzeigen
            if (countdownTMP != null) {
                countdownTMP.SetText(instructionText);
            }

            InitializeQuestionnaireToolkit();
            InitializePathsAndLogging();
            CacheControllers();
            ApplyConditionSequence();
        }

        void Update() {
            // Tastatur-Caching für Lesbarkeit
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;

            // Space startet den Countdown (nur wenn Experiment noch nicht läuft)
            if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame) {
                StartCountdown();
                Debug.Log("Space pressed: starting countdown.");
            }

            if (experimentRunning) {
                // Gesamtzeit des Experiments hochzählen
                experimentTime += Time.deltaTime;

                // Navigation zwischen Conditions im laufenden Experiment
                if (keyboard != null && keyboard.rightArrowKey.wasPressedThisFrame) {
                    NextCondition();
                }

                if (keyboard != null && keyboard.leftArrowKey.wasPressedThisFrame) {
                    PreviousCondition();
                }

                // Trial-Logik und Event-Logging
                if (trialRunning) {
                    HandleConditionTiming();
                    HandleEventLogging(mouse);
                    HandleFrameLogging();
                } else {
                    // Beispiel-Logik, falls Trigger betätigt wird, ohne dass Trial läuft
                    if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger)) {
                        Debug.Log("Primary Index Trigger clicked (no active trial).");
                    }
                }
            } else {
                // Countdown ablaufen lassen, solange Experiment noch nicht läuft
                if (!experimentRunning && countdownRunning) {
                    UpdateCountdown();
                }
            }
        }

        // ---------------------------------------------------------
        // Initialisierung
        // ---------------------------------------------------------
        private void InitializeQuestionnaireToolkit() {
            if (QTQuestionnaireManager != null) {
                qtManager = QTQuestionnaireManager.GetComponent<QTManager>();
            } else {
                Debug.Log("No questionnaires loaded.");
            }

            // Fragebogen-Canvas zu Beginn ausblenden
            if (QTCanvas != null) {
                QTCanvas.SetActive(false);
            }
        }

        private void InitializePathsAndLogging() {
            // persistent directory
            applicationPath = Application.persistentDataPath;
            var systemPath = applicationPath.TrimEnd('/') + "/" + loggingPath;

            Debug.Log("Logging path: " + systemPath);

            if (!Directory.Exists(systemPath)) {
                Directory.CreateDirectory(systemPath);
            }

            if (saveEventLog) {
                InitializeEventLog(systemPath);
            }

            if (saveFrameLog) {
                InitializeFrameLog(systemPath);
            }
        }

        private void InitializeEventLog(string systemPath) {
            string filePath = systemPath.TrimEnd('/') + "/" + "eventLog_" + System.DateTime.Now.Ticks + ".csv";
            eventLogFileWriter = new StreamWriter(filePath);
            eventLogFileWriter.AutoFlush = true;

            string headerLine = "TimeStamp,ExperimentName,SubjectID,Gender,Age,Recruitment,ConditionName,ConditionSequence,ConditionTime,ExperimentTime,Source,Target";

            // Add custom CSV event log items
            foreach (var eventItem in eventLogItems) {
                headerLine += "," + eventItem.headerName;
            }

            eventLogFileWriter.WriteLine(headerLine);
        }

        private void InitializeFrameLog(string systemPath) {
            string filePath = systemPath.TrimEnd('/') + "/" + "frameLog_" + System.DateTime.Now.Ticks + ".csv";
            frameLogFileWriter = new StreamWriter(filePath);
            frameLogFileWriter.AutoFlush = true;

            string headerLine = "TimeStamp,ExperimentName,SubjectID,Gender,Age,Recruitment,ConditionName,ConditionSequence,ConditionTime,ExperimentTime,SourceX,SourceY,SourceZ,TargetX,TargetY,TargetZ";

            // Add custom CSV frame log items
            foreach (var frameItem in frameLogItems) {
                headerLine += "," + frameItem.headerName;
            }

            frameLogFileWriter.WriteLine(headerLine);
        }

        private void CacheControllers() {
            // VR-Controller-Anker im Szenengraph cachen (falls vorhanden)
            ovrRightController = GameObject.Find("RightHandAnchor");
            ovrLeftController = GameObject.Find("LeftHandAnchor");
        }

        private void ApplyConditionSequence() {
            // Reihenfolge der Conditions je nach Studiendesign festlegen
            switch (ConditionSequence) {
                case SequenceOptions.BalancedLatinSquare:
                    conditions = GetBalancedLatinSquare(conditions, SubjectID);
                    break;

                case SequenceOptions.LatinSquare:
                    conditions = GetLatinSquare(conditions, SubjectID);
                    break;

                case SequenceOptions.Permutations:
                    conditions = GetAllPermutations(conditions, SubjectID);
                    break;

                case SequenceOptions.ShuffleBySeed:
                    conditions = Shuffle(conditions, SubjectID);
                    break;

                case SequenceOptions.Shuffle:
                    conditions = Shuffle(conditions);
                    break;
            }
        }

        // ---------------------------------------------------------
        // Countdown und Condition-Steuerung
        // ---------------------------------------------------------
        private void StartCountdown() {
            if (!experimentRunning && !countdownRunning) {
                countdownRunning = true;
            }
        }

        private void UpdateCountdown() {
            countdownTimer -= Time.deltaTime;

            int roundCountDownTimer = Mathf.RoundToInt(countdownTimer);
            if (countdownTMP != null) {
                countdownTMP.SetText(countdownText + " " + roundCountDownTimer);
            }

            if (roundCountDownTimer == 0) {
                countdownRunning = false;

                if (countdownTMP != null) {
                    countdownTMP.transform.parent.gameObject.SetActive(false);
                }

                ShowCondition(currentCondition);
            }
        }

        private void HandleConditionTiming() {
            // Zeit für die aktuelle Condition hochzählen
            conditionTime += Time.deltaTime;

            // Wenn Timer abgelaufen ist, Condition beenden
            if (useConditionTimer && conditionTime > conditionTimer) {
                Debug.Log("Condition stopped (timer reached).");

                if (QTQuestionnaireManager != null) {
                    ShowQuestionnaire();
                } else {
                    NextCondition();
                }
            }
        }

        private void HandleEventLogging(Mouse mouse) {
            if (!saveEventLog) {
                return;
            }

            // Maus-Klick Logging
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) {
                if (Camera.main != null) {
                    Vector2 mousePos = mouse.position.ReadValue();
                    Ray ray = Camera.main.ScreenPointToRay(mousePos);

                    if (Physics.Raycast(ray, out RaycastHit raycastHit, 100f)) {
                        if (raycastHit.transform != null) {
                            LogEvent(Camera.main.gameObject, raycastHit.transform.gameObject);
                        }
                    }
                }
            }

            // VR-Controller Logging
            if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger)) {
                HandleControllerRaycast(ovrRightController);
                HandleControllerRaycast(ovrLeftController);
            }
        }

        private void HandleControllerRaycast(GameObject controller) {
            if (controller == null) {
                return;
            }

            Ray ray = new Ray(controller.transform.position, controller.transform.forward);

            if (Physics.Raycast(ray, out RaycastHit raycastHit, 100f)) {
                if (raycastHit.transform != null) {
                    LogEvent(controller, raycastHit.transform.gameObject);
                }
            }
        }

        private void HandleFrameLogging() {
            if (!saveFrameLog) {
                return;
            }

            // Hier könnte pro Frame geloggt werden
            // Beispiel:
            // LogFrame(ovrLeftController, someTargetGameObject);
        }

        // ---------------------------------------------------------
        // Öffentliche Getter (z. B. für andere Komponenten)
        // ---------------------------------------------------------
        public string GetCurrentConditionName() {
            return currentConditionName;
        }

        public int GetCurrentCondition() {
            return currentCondition;
        }

        public int GetSubjectID() {
            return SubjectID;
        }

        public int GetAge() {
            return Age;
        }

        public string GetGender() {
            return Gender.ToString();
        }

        public string GetRecruitment() {
            return Recruitment.ToString();
        }

        // Beispiel-Methode für Custom CSV-Item
        public string getFPS() {
            return (1f / Time.unscaledDeltaTime).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        // ---------------------------------------------------------
        // Fragebögen
        // ---------------------------------------------------------
        public void NextQuestionnaire() {
            if (QTQuestionnaireManager == null || qtManager == null) {
                return;
            }

            Debug.Log("Showing next questionnaire...");

            // aktuellen Fragebogen schließen
            if (qtQManager != null) {
                qtQManager.HideQuestionnaire();
            }

            currentQuestionnaireNumber++;

            if (currentQuestionnaireNumber >= qtManager.questionnaires.Count) {
                Debug.LogWarning("No further questionnaire available.");
                return;
            }

            qtQManager = qtManager.questionnaires[currentQuestionnaireNumber];
            qtQManager.resultsSavePath = "/" + questionnairePath.TrimStart('/').TrimEnd('/') + "/";
            qtQManager.StartQuestionnaire();

            // Canvas bleibt an, solange Fragebögen angezeigt werden
            if (QTCanvas != null) {
                QTCanvas.SetActive(true);
            }
        }

        private void ShowQuestionnaire() {
            trialRunning = false;
            HideConditions();

            if (qtManager == null || qtManager.questionnaires == null || qtManager.questionnaires.Count == 0) {
                Debug.LogWarning("No questionnaire configured in QTManager.");
                return;
            }

            currentQuestionnaireNumber = Mathf.Clamp(currentQuestionnaireNumber, 0, qtManager.questionnaires.Count - 1);
            qtQManager = qtManager.questionnaires[currentQuestionnaireNumber];
            qtQManager.resultsSavePath = "/" + questionnairePath.TrimStart('/').TrimEnd('/') + "/";
            qtQManager.StartQuestionnaire();

            // Fragebogen-Canvas anzeigen
            if (QTCanvas != null) {
                QTCanvas.SetActive(true);
            }
        }

        // ---------------------------------------------------------
        // Condition wechseln
        // ---------------------------------------------------------
        private void PreviousCondition() {
            if (currentCondition > 0) {
                currentCondition--;
                ShowCondition(currentCondition);
            }
        }

        private void ShowCondition(int sequenceID) {
            // Condition starten
            conditionTime = 0f;
            experimentRunning = true;
            trialRunning = true;

            if (conditions == null || conditions.Length == 0) {
                Debug.LogWarning("No conditions assigned.");
                return;
            }

            if (currentCondition >= conditions.Length) {
                Debug.LogWarning("Current condition index is higher than number of conditions.");
                return;
            }

            sequenceID = currentCondition;

            for (int i = 0; i < conditions.Length; i++) {
                if (conditions[i] != null) {
                    conditions[i].SetActive(i == sequenceID);
                }
            }

            currentConditionName = conditions[sequenceID] != null ? conditions[sequenceID].name : "Undefined";

            Debug.Log("Current Subject: " + SubjectID +
                      " | Current Condition: " + sequenceID +
                      " | Condition Name: " + currentConditionName);

            // Falls ein Fragebogen-Canvas existiert, sicherheitshalber ausblenden,
            // da wir jetzt wieder in einer Condition sind.
            if (QTCanvas != null) {
                QTCanvas.SetActive(false);
            }
        }

        private void HideConditions() {
            conditionTime = 0f;
            trialRunning = false;

            if (conditions == null) {
                return;
            }

            for (int i = 0; i < conditions.Length; i++) {
                if (conditions[i] != null) {
                    conditions[i].SetActive(false);
                }
            }
        }

        public void NextCondition() {
            if (QTQuestionnaireManager != null && qtManager != null) {
                // alle Fragebögen zurücksetzen und verstecken
                foreach (var questionnaire in qtManager.questionnaires) {
                    questionnaire.ResetQuestionnaire();
                    questionnaire.HideQuestionnaire();
                }

                currentQuestionnaireNumber = 0;
            }

            // Fragebogen-Canvas ausblenden, wir gehen in eine neue Condition
            if (QTCanvas != null) {
                QTCanvas.SetActive(false);
            }

            if (currentCondition < conditions.Length - 1) {
                currentCondition++;
                ShowCondition(currentCondition);
            } else {
                StopRecording();

#if UNITY_EDITOR
                EditorApplication.isPlaying = false;
#elif UNITY_WEBPLAYER
                Application.OpenURL(webplayerQuitURL);
#else
                Application.Quit();
#endif
            }
        }

        // ---------------------------------------------------------
        // Logging
        // ---------------------------------------------------------
        private void StopRecording() {
            if (saveEventLog && eventLogFileWriter != null) {
                eventLogFileWriter.Close();
                eventLogFileWriter.Dispose();
                eventLogFileWriter = null;
            }

            if (saveFrameLog && frameLogFileWriter != null) {
                frameLogFileWriter.Close();
                frameLogFileWriter.Dispose();
                frameLogFileWriter = null;
            }
        }

        private void LogEvent(GameObject source, GameObject target) {
            if (!saveEventLog || eventLogFileWriter == null) {
                return;
            }

            string fileOutput =
                System.DateTime.Now.Ticks + "," +
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

            foreach (var eventItem in eventLogItems) {
                if (eventItem.itemValue.isAssigned) {
                    fileOutput += "," + eventItem.itemValue.GetOrInvoke();
                } else {
                    fileOutput += ",\"NULL\"";
                }
            }

            Debug.Log(fileOutput);
            eventLogFileWriter.WriteLine(fileOutput);
        }

        private void LogFrame(GameObject source, GameObject target) {
            if (!saveFrameLog || frameLogFileWriter == null) {
                return;
            }

            if (source == null || target == null) {
                return;
            }

            string fileOutput =
                System.DateTime.Now.Ticks + "," +
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

            foreach (var frameItem in frameLogItems) {
                if (frameItem.itemValue.isAssigned) {
                    fileOutput += "," + frameItem.itemValue.GetOrInvoke();
                } else {
                    fileOutput += ",\"NULL\"";
                }
            }

            Debug.Log(fileOutput);
            frameLogFileWriter.WriteLine(fileOutput);
        }

        // ---------------------------------------------------------
        // Sequenzfunktionen (Latin Square, Permutationen, Shuffle)
        // ---------------------------------------------------------
        public static T[] GetBalancedLatinSquare<T>(T[] array, int participant) {
            List<T> result = new List<T>();

            for (int i = 0, j = 0, h = 0; i < array.Length; ++i) {
                int val;

                if (i < 2 || i % 2 != 0) {
                    val = j++;
                } else {
                    val = array.Length - h - 1;
                    ++h;
                }

                int idx = (val + participant) % array.Length;
                result.Add(array[idx]);
            }

            if (array.Length % 2 != 0 && participant % 2 != 0) {
                result.Reverse();
            }

            return result.ToArray();
        }

        public static T[] GetLatinSquare<T>(T[] array, int participant) {
            int[,] latinSquare = new int[array.Length, array.Length];

            latinSquare[0, 0] = 1;
            latinSquare[0, 1] = 2;

            for (int i = 2, j = 3, k = 0; i < array.Length; i++) {
                if (i % 2 == 1) {
                    latinSquare[0, i] = j++;
                } else {
                    latinSquare[0, i] = array.Length - (k++);
                }
            }

            for (int i = 1; i <= array.Length; i++) {
                latinSquare[i - 1, 0] = i;
            }

            for (int row = 1; row < array.Length; row++) {
                for (int col = 1; col < array.Length; col++) {
                    latinSquare[row, col] = (latinSquare[row - 1, col] + 1) % array.Length;

                    if (latinSquare[row, col] == 0) {
                        latinSquare[row, col] = array.Length;
                    }
                }
            }

            T[] newArray = new T[array.Length];

            for (int col = 0; col < array.Length; col++) {
                int row = (participant + 1) % array.Length;
                newArray[col] = array[latinSquare[row, col] - 1];
            }

            return newArray;
        }

        public static T[] GetAllPermutations<T>(T[] array, int participant) {
            List<List<T>> results = GeneratePermutations(array.ToList());
            T[] newArray = new T[array.Length];

            int row = (participant + 1) % results.Count;

            for (int i = 0; i < results[row].Count; i++) {
                newArray[i] = results[row][i];
            }

            return newArray;
        }

        private static List<List<T>> GeneratePermutations<T>(List<T> items) {
            T[] currentPermutation = new T[items.Count];
            bool[] inSelection = new bool[items.Count];
            List<List<T>> results = new List<List<T>>();

            PermuteItems(items, inSelection, currentPermutation, results, 0);
            return results;
        }

        private static void PermuteItems<T>(List<T> items, bool[] inSelection, T[] currentPermutation, List<List<T>> results, int nextPosition) {
            if (nextPosition == items.Count) {
                results.Add(currentPermutation.ToList());
            } else {
                for (int i = 0; i < items.Count; i++) {
                    if (!inSelection[i]) {
                        inSelection[i] = true;
                        currentPermutation[nextPosition] = items[i];
                        PermuteItems(items, inSelection, currentPermutation, results, nextPosition + 1);
                        inSelection[i] = false;
                    }
                }
            }
        }

        public static T[] Shuffle<T>(T[] array) {
            int n = array.Length;

            for (int i = 0; i < n; i++) {
                int r = i + (int)(Random.Range(0.0f, 1.0f) * (n - i));
                T t = array[r];
                array[r] = array[i];
                array[i] = t;
            }

            return array;
        }

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

    // ---------------------------------------------------------
    // Inspector Attribute für ReadOnly-Felder
    // ---------------------------------------------------------
    public class ReadOnlyAttribute : PropertyAttribute { }
    public class ShowOnlyAttribute : PropertyAttribute { }

#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(ReadOnlyAttribute))]
    public class ReadOnlyDrawer : PropertyDrawer {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
            return EditorGUI.GetPropertyHeight(property, label, true);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
            GUI.enabled = false;
            EditorGUI.PropertyField(position, property, label, true);
            GUI.enabled = true;
        }
    }
#endif
}
