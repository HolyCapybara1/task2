using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;

namespace PhotoAnnotation
{
    public sealed class SQLiteDb : IDisposable
    {
        private readonly string _dbPath;
        private readonly SQLiteConnection _conn;
        public string BaseDirectory { get; }

        private SQLiteDb(string dbPath, SQLiteConnection conn)
        {
            _dbPath = dbPath;
            _conn = conn;
            BaseDirectory = Path.GetDirectoryName(_dbPath) ?? AppDomain.CurrentDomain.BaseDirectory;
        }

        public static SQLiteDb OpenOrCreate(string dbPath)
        {
            if (string.IsNullOrWhiteSpace(dbPath))
                throw new ArgumentException("dbPath is null/empty");

            dbPath = Path.GetFullPath(dbPath);
            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var isNew = !File.Exists(dbPath);
            if (isNew)
                SQLiteConnection.CreateFile(dbPath);

            var csb = new SQLiteConnectionStringBuilder
            {
                DataSource = dbPath,
                ForeignKeys = true,
                JournalMode = SQLiteJournalModeEnum.Wal,
                SyncMode = SynchronizationModes.Normal
            };

            var conn = new SQLiteConnection(csb.ToString());
            conn.Open();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA foreign_keys = ON;";
                cmd.ExecuteNonQuery();
            }

            EnsureCreated(conn);       // создаём схему, если её нет
            if (isNew) SeedIfEmpty(conn); // можно убрать, если сид не нужен

            return new SQLiteDb(dbPath, conn);
        }

        private static void EnsureCreated(SQLiteConnection conn)
        {
            const string schema = @"
PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS Images (
  Id           INTEGER PRIMARY KEY,
  FilePath     TEXT NOT NULL UNIQUE,
  DisplayName  TEXT
);

CREATE TABLE IF NOT EXISTS Questions (
  Id         INTEGER PRIMARY KEY,
  Text       TEXT NOT NULL,
  SortOrder  INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS Answers (
  ImageId     INTEGER NOT NULL,
  QuestionId  INTEGER NOT NULL,
  Value       INTEGER NOT NULL CHECK (Value IN (0,1,2)),
  AnsweredAt  TEXT NOT NULL DEFAULT (datetime('now')),
  PRIMARY KEY (ImageId, QuestionId),
  FOREIGN KEY (ImageId) REFERENCES Images(Id) ON DELETE CASCADE,
  FOREIGN KEY (QuestionId) REFERENCES Questions(Id) ON DELETE CASCADE
);
";
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = schema;
                cmd.ExecuteNonQuery();
            }
        }

        
        private static void SeedIfEmpty(SQLiteConnection conn)
        {
            bool hasQuestions;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM Questions LIMIT 1);";
                hasQuestions = Convert.ToInt32(cmd.ExecuteScalar()) == 1;
            }
            if (!hasQuestions)
            {
                using (var tx = conn.BeginTransaction())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "INSERT INTO Questions(Text, SortOrder) VALUES (@t,@s);";
                    var pT = cmd.CreateParameter(); pT.ParameterName = "@t"; cmd.Parameters.Add(pT);
                    var pS = cmd.CreateParameter(); pS.ParameterName = "@s"; cmd.Parameters.Add(pS);

                    string[] texts = { "Объект виден?", "Наличие дефекта?", "Не уверен(а)?" };
                    for (int i = 0; i < texts.Length; i++)
                    {
                        pT.Value = texts[i];
                        pS.Value = i;
                        cmd.ExecuteNonQuery();
                    }
                    tx.Commit();
                }
            }
        }

        public void Dispose() { _conn?.Dispose(); }

        // ------- ПУБЛИЧНЫЕ API-------

        public IList<Question> LoadQuestions()
        {
            var list = new List<Question>();
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Id, Text, SortOrder FROM Questions ORDER BY SortOrder, Id;";
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        list.Add(new Question
                        {
                            Id = r.GetInt32(0),
                            Text = r.GetString(1),
                            SortOrder = r.IsDBNull(2) ? 0 : r.GetInt32(2)
                        });
                    }
                }
            }
            return list;
        }

        public IList<ImageItem> LoadImages()
        {
            var list = new List<ImageItem>();
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Id, FilePath, DisplayName FROM Images ORDER BY Id;";
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        list.Add(new ImageItem
                        {
                            Id = r.GetInt32(0),
                            FilePath = r.GetString(1),
                            DisplayName = r.IsDBNull(2) ? null : r.GetString(2)
                        });
                    }
                }
            }
            return list;
        }

        public Dictionary<int, AnswerValue> LoadAnswersForImage(int imageId)
        {
            var map = new Dictionary<int, AnswerValue>();
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = "SELECT QuestionId, Value FROM Answers WHERE ImageId = @id;";
                cmd.Parameters.AddWithValue("@id", imageId);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        map[r.GetInt32(0)] = (AnswerValue)r.GetInt32(1);
                    }
                }
            }
            return map;
        }

        public void SaveAnswers(int imageId, IDictionary<int, AnswerValue> answers)
        {
            if (answers == null || answers.Count == 0) return;

            using (var tx = _conn.BeginTransaction())
            using (var cmd = _conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText =
                    "INSERT OR REPLACE INTO Answers (ImageId, QuestionId, Value, AnsweredAt) " +
                    "VALUES (@img, @q, @v, datetime('now'));";

                var pImg = cmd.CreateParameter(); pImg.ParameterName = "@img"; cmd.Parameters.Add(pImg);
                var pQ = cmd.CreateParameter(); pQ.ParameterName = "@q"; cmd.Parameters.Add(pQ);
                var pV = cmd.CreateParameter(); pV.ParameterName = "@v"; cmd.Parameters.Add(pV);

                foreach (var kv in answers)
                {
                    pImg.Value = imageId;
                    pQ.Value = kv.Key;
                    pV.Value = (int)kv.Value;
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
            }
        }

        public int UpsertImage(string filePath, string displayName)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException(nameof(filePath));
            using (var tx = _conn.BeginTransaction())
            using (var cmd = _conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
INSERT INTO Images(FilePath, DisplayName) VALUES(@p,@d)
ON CONFLICT(FilePath) DO UPDATE SET DisplayName=excluded.DisplayName;
SELECT Id FROM Images WHERE FilePath=@p;";
                cmd.Parameters.AddWithValue("@p", filePath);
                cmd.Parameters.AddWithValue("@d", (object)displayName ?? DBNull.Value);
                var id = Convert.ToInt32(cmd.ExecuteScalar());
                tx.Commit();
                return id;
            }
        }

        public int UpsertQuestion(string text, int sortOrder)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException(nameof(text));
            using (var tx = _conn.BeginTransaction())
            using (var cmd = _conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
INSERT INTO Questions(Text, SortOrder) VALUES(@t,@s);
SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("@t", text);
                cmd.Parameters.AddWithValue("@s", sortOrder);
                var id = Convert.ToInt32(cmd.ExecuteScalar());
                tx.Commit();
                return id;
            }
        }

        public string ResolveImagePath(string dbFilePathOrRelative)
        {
            if (string.IsNullOrWhiteSpace(dbFilePathOrRelative)) return null;
            var path = dbFilePathOrRelative;
            if (!Path.IsPathRooted(path))
                path = Path.Combine(BaseDirectory, path);
            return Path.GetFullPath(path);
        }
    }
}
