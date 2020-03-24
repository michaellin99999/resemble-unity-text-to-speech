using System.Linq;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Resemble;
using Resemble.Structs;

namespace Resemble.GUIEditor
{
    [CustomEditor(typeof(Clip)), CanEditMultipleObjects]
    public class Clip_Editor : Editor
    {
        //Data
        private Clip clip;
        private Editor clipEditor;
        private AsyncRequest request;
        private const float checkCooldown = 1.5f;  //Time in seconds between 2 checks

        //GUI
        private Rect renameRect;
        private bool clipPlaying;
        private bool haveUserData;
        private bool showRawPhonemes;
        public Text_Editor drawer;
        private GUIContent userData = new GUIContent("UserData", "This area is available to make your life easier. Put whatever you want in it. You can retrieve it in game via YourClip.userData.");
        private GUIContent phonemes = new GUIContent("Phonemes", "You will find here the phonemes pronounced by the generated voice.");
        private double lastCheckTime;
        
        protected override void OnHeaderGUI()
        {
            //Init resources
            InitComponents();

            //Bar
            Rect rect = new Rect(0, 0, Screen.width, 46);
            GUI.Box(rect, "", Styles.bigTitle);

            //Icon
            rect.Set(6, 6, 32, 32);
            GUI.DrawTexture(rect, Resources.instance.icon);

            //Speech name and shorcut
            float width = Styles.header.CalcSize(new GUIContent(clip.speech.name + " > ")).x;
            rect.Set(44, 4, width, 22);
            if (GUI.Button(rect, clip.speech.name + " > ", Styles.header))
                Selection.activeObject = clip.speech;
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);

            //Clip name and rename
            rect.Set(rect.x + rect.width, rect.y, Screen.width - (rect.x + rect.width + 50), rect.height);
            renameRect = rect;
            if (GUI.Button(rect, clip.clipName, Styles.header))
                Rename();
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);

            //Resemble.ai link
            rect.Set(Screen.width - 140, rect.y + 24, 135, 16);
            if (GUI.Button(rect, "Show in Resemble.ai", EditorStyles.linkLabel))
                WebPage.ResembleProjects.Open(Settings.projectUUID + "/clips/" + clip.uuid);
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);

            //Help button
            rect.Set(Screen.width - 37, 6, 16, 16);
            if (GUI.Button(rect, Styles.characterSetHelpBtn, GUIStyle.none))
                WebPage.PluginDoc.Open();

            //Options button
            rect.x += 18;
            if (GUI.Button(rect, Styles.popupBtn, GUIStyle.none))
            {
                GenericMenu menu = new GenericMenu();
                menu.AddItem(new GUIContent("Update from API"), false, UpdateFromAPI);
                menu.AddItem(new GUIContent("Generate Audio"), false, () => { request = AsyncRequest.Make(clip); });
                menu.AddItem(new GUIContent("Download wav as..."), false, ExportClip);
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Rename"), false, Rename);
                menu.AddItem(new GUIContent("Delete"), false, Delete);
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Help"), false, () => { WebPage.PluginClip.Open(); });
                menu.AddItem(new GUIContent("Settings"), false, Settings.OpenWindow);
                menu.DropDown(rect);
            }
            GUILayout.Space(50);
        }

        public override void OnInspectorGUI()
        {
            //Draw text area
            GUILayout.BeginVertical(EditorStyles.helpBox);
            DrawTextArea();
            GUILayout.EndVertical();

            //Error box
            if (request != null && request.status == AsyncRequest.Status.Error)
            {
                request.error.DrawErrorBox();
                Rect rect = GUILayoutUtility.GetLastRect();
                rect.Set(rect.x + rect.width - 70, rect.y + rect.height - 20, 68, 18);
                if (GUI.Button(rect, "Close"))
                {
                    request.status = AsyncRequest.Status.Completed;
                    AsyncRequest.RegisterRefreshEvent();
                }
            }

            //Draw audio area
            GUILayout.Space(10);
            GUILayout.BeginVertical(EditorStyles.helpBox);
            DrawAudioArea();
            GUILayout.EndVertical();

            //Draw phonemes area
            if (clip.speech.includePhonemes)
            {
                GUILayout.Space(10);
                GUILayout.BeginVertical(EditorStyles.helpBox);
                DrawPhonemesArea();
                GUILayout.EndVertical();
            }

            //Draw userdata area
            GUILayout.Space(10);
            GUILayout.BeginVertical(EditorStyles.helpBox);
            DrawUserDataArea();
            GUILayout.EndVertical();

            //Drax connection message
            Utils.ConnectionRequireMessage();
            GUILayout.Space(30);

            //TEMP (still necessary?) Keep refreshing this window 
            Repaint();
        }

        private void DrawTextArea()
        {
            //Tags buttons
            drawer.DrawTagsBtnsLayout(false);

            //Separator
            Rect rect = GUILayoutUtility.GetRect(1.0f, 1.0f, GUILayout.ExpandWidth(true));
            rect.Set(rect.x, rect.y, rect.width, 1.0f);
            EditorGUI.DrawRect(rect, Color.grey * 0.2f);

            //Draw text field
            drawer.DoLayout(true, false);

            //Draw character count bar
            rect = GUILayoutUtility.GetRect(Screen.width, 25).Shrink(10);
            rect.Set(rect.x, rect.y - 10, rect.width, rect.height + 10);
            drawer.DrawCharCountBar(rect);

            //Draw bottom text buttons
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (request == null)
            {
                EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(clip.text.userString));
                if (GUILayout.Button("Generate Audio"))
                    request = AsyncRequest.Make(clip);
                EditorGUI.EndDisabledGroup();
            }
            else
            {
                if (GUILayout.Button("Select parent"))
                    Selection.activeObject = clip.speech;
            }

            GUILayout.EndHorizontal();
        }

        private void DrawAudioArea()
        {
            //Remove request if completed
            if (request != null && request.status == AsyncRequest.Status.Completed)
                request = null;

            //Draw pending message or dowload progress bar
            if (request != null && !request.isDone)
            {
                if (request.status != AsyncRequest.Status.Downloading)
                {
                    GUILayout.Space(4);
                    Utils.DrawPendingLabel("Pending...");
                    GUILayout.Space(-10);
                    Utils.DrawSeparator();
                    GUILayout.Space(-5);
                }
                else
                {
                    GUILayout.Space(10);
                    Rect rect = GUILayoutUtility.GetRect(Screen.width, 30);
                    float progress = request.downloadProgress;
                    EditorGUI.ProgressBar(rect, progress, "Download : " + Mathf.RoundToInt(progress * 100) + "%");
                }
            }

            //Draw Audio preview
            else if (clip.clip != null)
            {
                DrawAudioPlayer();
            }

            //Draw commands
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (request != null)
            {
                if (GUILayout.Button("Show pending list"))
                {
                    Resemble_Window.Open(Resemble_Window.Tab.Pool);
                }
                if (clip.clip != null && GUILayout.Button("Select placeholder"))
                {
                    EditorGUIUtility.PingObject(clip.clip);
                    Selection.activeObject = clip.clip;
                }
            }
            else if (clip.clip != null)
            {
                if (GUILayout.Button("Select file"))
                {
                    EditorGUIUtility.PingObject(clip.clip);
                    Selection.activeObject = clip.clip;
                }
            }
            GUILayout.EndHorizontal();
        }

        private void DrawPhonemesArea()
        {
            //Toolbar
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Space(-4);
            GUILayout.Label(phonemes, EditorStyles.toolbarButton);
            GUILayout.FlexibleSpace();
            bool haveTable = clip.speech.phonemeTable != null;

            if (GUILayout.Toggle(showRawPhonemes, "Raw", EditorStyles.toolbarButton) || !haveTable)
                showRawPhonemes = true;

            EditorGUI.BeginDisabledGroup(!haveTable);
            if (GUILayout.Toggle(!showRawPhonemes, "Refined", EditorStyles.toolbarButton))
                showRawPhonemes = false;
            EditorGUI.EndDisabledGroup();

            GUILayout.Space(-4);
            GUILayout.EndHorizontal();
            GUILayout.Space(10);


            //Draw preview
            Rect rect = GUILayoutUtility.GetRect(Screen.width, 100);
            EditorGUI.DrawRect(rect, Color.black * 0.7f);
        }

        private void DrawUserDataArea()
        {
            //Toolbar
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Space(-4);
            GUILayout.Label(userData, EditorStyles.toolbarButton);
            GUILayout.FlexibleSpace();
            if (haveUserData && GUILayout.Button("Remove text", EditorStyles.toolbarButton))
            {
                haveUserData = false;
                clip.userdata = "";
                EditorUtility.SetDirty(clip);
            }
            if (!haveUserData && GUILayout.Button("Add text", EditorStyles.toolbarButton))
                haveUserData = true;
            int id = 0;
            if (GUILayout.Button("Add Label", EditorStyles.toolbarButton))
            {
                string name = "New label";
                while (clip.ContainsLabel(name))
                {
                    id++;
                    name = "New Label " + id;
                }
                ArrayUtility.Add(ref clip.labels, new Label(name, 0));
                EditorUtility.SetDirty(clip);
            }
            GUILayout.Space(-4);
            GUILayout.EndHorizontal();


            //User data string
            if (haveUserData)
            {
                GUILayout.Space(10);
                clip.userdata = GUILayout.TextArea(clip.userdata, GUILayout.MinHeight(100));
            }

            //Draw labels
            if (clip.labels != null && clip.labels.Length > 0)
            {
                GUILayout.Space(10);
                int count = clip.labels.Length;
                for (int i = 0; i < count; i++)
                {
                    EditorGUI.BeginChangeCheck();
                    GUILayout.BeginHorizontal();
                    string labelText = GUILayout.TextField(clip.labels[i].text);
                    int labelValue = EditorGUILayout.IntField(clip.labels[i].value, GUILayout.Width(50));
                    bool delete = GUILayout.Button("X", GUILayout.Width(20));
                    GUILayout.EndHorizontal();

                    if (EditorGUI.EndChangeCheck())
                    {
                        if (delete)
                        {
                            ArrayUtility.RemoveAt(ref clip.labels, i);
                            count--;
                            i--;
                        }
                        else
                        {
                            clip.labels[i] = new Label(labelText, labelValue);
                            EditorUtility.SetDirty(clip);
                        }
                    }
                }
                GUILayout.Space(10);
            }
        }

        private void InitComponents()
        {
            Styles.Load();
            clip = target as Clip;
            if (clip.text == null)
                clip.text = new Text();
            if (drawer == null)
                drawer = new Text_Editor(clip.text, SetDirty, Repaint);
        }

        private void OnEditText()
        {
            EditorUtility.SetDirty(clip);
        }

        public void OnEnable()
        {
            System.Reflection.FieldInfo info = typeof(EditorApplication).GetField("globalEventHandler", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            EditorApplication.CallbackFunction value = (EditorApplication.CallbackFunction)info.GetValue(null);
            value += HandleDeleteEvent;
            info.SetValue(null, value);
            InitComponents();
            request = AsyncRequest.Get(clip.uuid);
            haveUserData = !string.IsNullOrEmpty(clip.userdata);
            drawer.Refresh();
            Repaint();
        }

        public void OnDisable()
        {
            System.Reflection.FieldInfo info = typeof(EditorApplication).GetField("globalEventHandler", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            EditorApplication.CallbackFunction value = (EditorApplication.CallbackFunction)info.GetValue(null);
            value -= HandleDeleteEvent;
            info.SetValue(null, value);
        }

        private void HandleDeleteEvent()
        {
            Event e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Delete)
            {
                Object[] pods = Selection.objects.Where(x => x is Clip).ToArray();
                if (pods.Length == 1 && pods[0] == clip)
                    Delete();
                else if (pods.Length > 1)
                    DeleteMultiple(pods);
            }
        }

        public static bool RenameableField(Rect rect, ref bool rename, ref string renameLabel, string originName, out int controlID)
        {
            KeyCode keycode = KeyCode.A;
            controlID = GUIUtility.GetControlID("renameLabel".GetHashCode(), FocusType.Passive, rect);
            if (Event.current.GetTypeForControl(controlID) == EventType.KeyDown)
                keycode = Event.current.keyCode;
            renameLabel = GUI.TextField(rect, rename ? renameLabel : originName, rename ? Styles.headerField : Styles.header);
            TextEditor textEdit = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);

            switch (keycode)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (rename)
                    {
                        controlID = 0;
                        GUI.FocusControl("None");
                    }
                    break;
                case KeyCode.Escape:
                    if (rename)
                    {
                        controlID = 0;
                        renameLabel = originName;
                        GUI.FocusControl("None");
                    }
                    break;
            }

            if (controlID != textEdit.controlID - 1)
            {
                if (rename)
                {
                    rename = false;
                    return true;
                }
            }
            else
            {
                if (!rename)
                {
                    rename = true;
                    renameLabel = originName;
                }
            }
            return false;
        }

        public void DrawAudioPlayer()
        {
            if (clipEditor == null || clipEditor.target != clip.clip)
            {
                clipEditor = Editor.CreateEditor(clip.clip);
            }

            //Get playing clip data
            int sample = 0;
            float time = 0.0f;
            if (clipPlaying)
            {
                clipPlaying = AudioPreview.IsClipPlaying(clip.clip);
                sample = AudioPreview.GetClipSamplePosition(clip.clip);
                time = sample / (float)clip.clip.samples;
            }

            //Preview toolbar
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Space(-4);

            if (!clipPlaying)
            {
                if (GUILayout.Button("Play", EditorStyles.toolbarButton))
                {
                    AudioPreview.PlayClip(clip.clip);
                    clipPlaying = true;
                }
            }
            else
            {
                if (GUILayout.Button("Stop", EditorStyles.toolbarButton))
                {
                    AudioPreview.StopClip(clip.clip);
                    clipPlaying = false;
                }
            }

            GUILayout.FlexibleSpace();

            //Draw clip length label
            float clipLength = clip.clip.length;
            GUILayout.Label((time * clipLength).ToString("0:00") + "/" + clipLength.ToString("0:00"));

            GUILayout.EndHorizontal();
            GUILayout.Space(10);

            //Draw preview
            Rect rect = GUILayoutUtility.GetRect(Screen.width, 100);
            
            //Allows you to launch the clip at a given timming.
            if (GUI.Button(rect, "", GUIStyle.none))
            {
                time = Rect.PointToNormalized(rect, Event.current.mousePosition).x;
                sample = Mathf.RoundToInt(time * clip.clip.samples);
                if (clipPlaying)
                    AudioPreview.StopClip(clip.clip);
                AudioPreview.PlayClip(clip.clip, sample);
                AudioPreview.SetClipSamplePosition(clip.clip, sample);
                clipPlaying = true;
            }

            //Draw clip spectrum
            clipEditor.OnPreviewGUI(rect, GUI.skin.box);

            //Draw progress bar
            if (clipPlaying)
            {
                rect.Set(rect.x + rect.width * time, rect.y, 2, rect.height);
                EditorGUI.DrawRect(rect, Color.white);
                Repaint();
            }
        }

        public void UpdateFromAPI()
        {
            if (!EditorUtility.DisplayDialog("Update from API", "This operation will overwrite existing " +
                "information with information from the Resemble.ai website.", "Ok", "Cancel"))
                return;

            APIBridge.GetClip(clip.uuid, (ResembleClip clip, Error error) =>
            {
                if (error)
                    error.Log();
                else
                {
                    this.clip.text.ParseResembleString(clip.body);
                    this.clip.clipName = clip.title;
                    this.clip.name = clip.uuid + "-" + clip.title;
                    drawer.Refresh();
                    Repaint();
                }
            });
        }

        public void ExportClip()
        {

        }

        public void Rename()
        {
            StringPopup.Show(GUIUtility.GUIToScreenRect(renameRect.Offset(0, 20, 0, 0)),
            "Rename clip", clip.clipName, (string value) => {
                if (!string.IsNullOrEmpty(clip.clipName) && value != clip.clipName)
                    Rename(value);
            });
        }

        public void Delete()
        {
            clip = target as Clip;
            string path = AssetDatabase.GetAssetPath(clip);
            if (!EditorUtility.DisplayDialog("Delete pod ?", path + "\nYou cannot undo this action.", "Delete", "Cancel"))
                return;
            AssetDatabase.RemoveObjectFromAsset(clip);
            EditorUtility.SetDirty(AssetDatabase.LoadAssetAtPath<Speech>(path));
            AssetDatabase.ImportAsset(path);
        }

        public void DeleteMultiple(Object[] targets)
        {
            string[] paths = targets.Select(x => AssetDatabase.GetAssetPath(x)).ToArray();
            string allPath = "";
            for (int i = 0; i < paths.Length; i++)
            {
                if (i == 3)
                {
                    allPath += "...";
                    break;
                }
                allPath += paths[i] + "/" + targets[i].name + "\n";
            }
            if (!EditorUtility.DisplayDialog("Delete pods ?", allPath + "\nYou cannot undo this action.", "Delete", "Cancel"))
                return;

            List<string> sets = new List<string>();
            for (int i = 0; i < targets.Length; i++)
            {
                AssetDatabase.RemoveObjectFromAsset(targets[i]);
                if (!sets.Contains(paths[i]))
                    sets.Add(paths[i]);
            }

            for (int i = 0; i < sets.Count; i++)
            {
                EditorUtility.SetDirty(AssetDatabase.LoadAssetAtPath<Speech>(sets[i]));
                AssetDatabase.ImportAsset(sets[i]);
            }
        }

        public void Rename(string value)
        {
            clip.clipName = value;
            clip.name = clip.uuid + "-" + value;
            SetDirty();
            ReImport();
        }

        public new void SetDirty()
        {
            EditorUtility.SetDirty(clip);
        }

        public void ReImport()
        {
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(clip), ImportAssetOptions.ForceUpdate);
        }

    }
}