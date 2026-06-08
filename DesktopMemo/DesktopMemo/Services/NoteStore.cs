using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using DesktopMemo.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DesktopMemo.Services
{
    public class NoteStore
    {
        private static NoteStore _instance;
        public static NoteStore Instance => _instance ?? (_instance = new NoteStore());

        private readonly string _dataDir;
        private readonly string _notesFile;
        private readonly string _settingsFile;
        private readonly object _lock = new object();
        private Timer _saveTimer;

        public List<Note> Notes { get; private set; } = new List<Note>();
        public AppSettings Settings { get; private set; } = new AppSettings();

        public event Action<Note> NoteAdded;
        public event Action<Note> NoteUpdated;
        public event Action<string> NoteDeleted;
        public event Action NotesLoaded;

        private NoteStore()
        {
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;
            _dataDir = Path.Combine(exeDir, "data");
            _notesFile = Path.Combine(_dataDir, "notes.json");
            _settingsFile = Path.Combine(_dataDir, "settings.json");

            if (!Directory.Exists(_dataDir))
                Directory.CreateDirectory(_dataDir);

            Load();
        }

        public void Load()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(_notesFile))
                    {
                        var json = File.ReadAllText(_notesFile);
                        Notes = JsonConvert.DeserializeObject<List<Note>>(json) ?? new List<Note>();
                    }

                    if (File.Exists(_settingsFile))
                    {
                        var json = File.ReadAllText(_settingsFile);
                        Settings = JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error loading data: {ex.Message}");
                    Notes = new List<Note>();
                    Settings = new AppSettings();
                }

                NotesLoaded?.Invoke();
            }
        }

        private void ScheduleSave()
        {
            _saveTimer?.Dispose();
            _saveTimer = new Timer(_ => Save(), null, 500, Timeout.Infinite);
        }

        public void Save()
        {
            lock (_lock)
            {
                try
                {
                    SaveFile(_notesFile, JsonConvert.SerializeObject(Notes, Formatting.Indented));
                    SaveFile(_settingsFile, JsonConvert.SerializeObject(Settings, Formatting.Indented));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error saving data: {ex.Message}");
                }
            }
        }

        private static void SaveFile(string path, string content)
        {
            var tmpFile = path + ".tmp";
            File.WriteAllText(tmpFile, content);
            if (File.Exists(path))
                File.Replace(tmpFile, path, null);
            else
                File.Move(tmpFile, path);
        }

        public Note AddNote(Note note = null)
        {
            note = note ?? new Note { Color = Settings.DefaultColor };
            if (string.IsNullOrEmpty(note.Id))
                note.Id = Guid.NewGuid().ToString();
            note.CreatedAt = DateTime.Now;
            note.UpdatedAt = DateTime.Now;

            lock (_lock)
            {
                Notes.Add(note);
            }

            ScheduleSave();
            NoteAdded?.Invoke(note);
            return note;
        }

        public Note UpdateNote(string id, string jsonBody)
        {
            lock (_lock)
            {
                var existing = Notes.FirstOrDefault(n => n.Id == id);
                if (existing == null) return null;

                var jObj = JObject.Parse(jsonBody);

                // Only update fields that are present in the JSON
                if (jObj.ContainsKey("title")) existing.Title = (string)jObj["title"];
                if (jObj.ContainsKey("content")) existing.Content = (string)jObj["content"];
                if (jObj.ContainsKey("color")) existing.Color = (string)jObj["color"];
                if (jObj.ContainsKey("x")) existing.X = (double)jObj["x"];
                if (jObj.ContainsKey("y")) existing.Y = (double)jObj["y"];
                if (jObj.ContainsKey("width")) existing.Width = (double)jObj["width"];
                if (jObj.ContainsKey("height")) existing.Height = (double)jObj["height"];
                if (jObj.ContainsKey("alwaysOnTop")) existing.AlwaysOnTop = (bool)jObj["alwaysOnTop"];
                if (jObj.ContainsKey("visible")) existing.Visible = (bool)jObj["visible"];

                existing.UpdatedAt = DateTime.Now;

                ScheduleSave();
                NoteUpdated?.Invoke(existing);
                return existing;
            }
        }

        public bool DeleteNote(string id)
        {
            lock (_lock)
            {
                var note = Notes.FirstOrDefault(n => n.Id == id);
                if (note == null) return false;

                Notes.Remove(note);
                ScheduleSave();
                NoteDeleted?.Invoke(id);
                return true;
            }
        }

        public Note GetNote(string id)
        {
            lock (_lock)
            {
                return Notes.FirstOrDefault(n => n.Id == id);
            }
        }

        public List<Note> GetAllNotes()
        {
            lock (_lock)
            {
                return new List<Note>(Notes);
            }
        }
    }
}
