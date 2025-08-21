using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace PhotoAnnotation
{
    public partial class MainForm : Form
    {
        // ---- ДАННЫЕ ----
        private SQLiteDb _db;
        private List<Question> _questions = new List<Question>();
        private List<ImageItem> _images = new List<ImageItem>();
        private int _index = 0;

        private sealed class QuestionRow
        {
            public int QuestionId;
            public RadioButton RbYes;
            public RadioButton RbNo;
            public RadioButton RbUnknown;
        }
        private readonly Dictionary<int, QuestionRow> _rows = new Dictionary<int, QuestionRow>();

        // ---- КОНТРОЛЫ (создаём программно) ----
        private SplitContainer ui_split;
        private Panel ui_panelImage;
        private PictureBox ui_picture;

        private Panel ui_panelTopBar;
        private Button ui_btnImport;
        private Button ui_btnPrev;
        private Button ui_btnSaveNext;
        private Label ui_lblProgress;
        private Label ui_lblFileName;

        private Panel ui_panelBottom;
        private TableLayoutPanel ui_tblQuestions;

        public MainForm()
        {
            
            Text = "ФотоАннотация";
            StartPosition = FormStartPosition.CenterScreen;
            WindowState = FormWindowState.Maximized;

            BuildUi();

            // Подписки
            Load += MainForm_Load;
            FormClosing += MainForm_FormClosing;

            ui_btnImport.Click += Ui_btnImport_Click;
            ui_btnPrev.Click += Ui_btnPrev_Click;
            ui_btnSaveNext.Click += Ui_btnSaveNext_Click; // <-- единственный обработчик
        }

        // ================== UI ==================
        private void BuildUi()
        {
            // SplitContainer (горизонтально)
            ui_split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 6
            };
            Controls.Add(ui_split);

            // Верхняя панель: изображение
            ui_panelImage = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            ui_split.Panel1.Controls.Add(ui_panelImage);

            ui_picture = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle
            };
            ui_panelImage.Controls.Add(ui_picture);

            // Нижняя панель
            ui_panelBottom = new Panel { Dock = DockStyle.Fill };
            ui_split.Panel2.Controls.Add(ui_panelBottom);

            // Топ-бар
            ui_panelTopBar = new Panel { Dock = DockStyle.Top, Height = 44 };
            ui_panelBottom.Controls.Add(ui_panelTopBar);

            ui_btnImport = new Button
            {
                Text = "Загрузить изображения…",
                AutoSize = true,
                Location = new Point(8, 8)
            };
            ui_panelTopBar.Controls.Add(ui_btnImport);

            ui_lblProgress = new Label { AutoSize = true, Text = "0 / 0" };
            ui_lblFileName = new Label { AutoSize = true, Text = "--" };
            ui_panelTopBar.Controls.Add(ui_lblProgress);
            ui_panelTopBar.Controls.Add(ui_lblFileName);
            ui_panelTopBar.Resize += (s, e) =>
            {
                ui_lblProgress.Location = new Point((ui_panelTopBar.Width / 2) - 40, 12);
                ui_lblFileName.Location = new Point((ui_panelTopBar.Width / 2) + 30, 12);
            };
            ui_lblProgress.Location = new Point((ui_panelTopBar.Width / 2) - 40, 12);
            ui_lblFileName.Location = new Point((ui_panelTopBar.Width / 2) + 30, 12);

            // Кнопки снизу
            var bottomButtons = new Panel { Dock = DockStyle.Bottom, Height = 50 };
            ui_panelBottom.Controls.Add(bottomButtons);

            ui_btnPrev = new Button
            {
                Text = "◀ Назад",
                AutoSize = true,
                Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
                Location = new Point(8, 10)
            };
            bottomButtons.Controls.Add(ui_btnPrev);

            ui_btnSaveNext = new Button
            {
                Text = "Сохранить и далее ▶",
                AutoSize = true,
                Anchor = AnchorStyles.Right | AnchorStyles.Bottom
            };
            bottomButtons.Controls.Add(ui_btnSaveNext);
            bottomButtons.Resize += (s, e) =>
            {
                ui_btnSaveNext.Location = new Point(bottomButtons.Width - ui_btnSaveNext.Width - 8, 10);
            };

            // Таблица вопросов
            ui_tblQuestions = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                GrowStyle = TableLayoutPanelGrowStyle.AddRows,
                ColumnCount = 2,
                Padding = new Padding(6)
            };
            ui_tblQuestions.ColumnStyles.Clear();
            ui_tblQuestions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));        // текст вопроса
            ui_tblQuestions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));   // ответы
            ui_panelBottom.Controls.Add(ui_tblQuestions);
            ui_tblQuestions.BringToFront();

            // деление сплита
            ui_split.SplitterDistance = (int)(Height * 0.65);
        }

        // ================== ЖИЗНЕННЫЙ ЦИКЛ ==================
        private void MainForm_Load(object sender, EventArgs e)
        {
            try
            {
                var dbPath = AppPaths.GetDefaultDbPath();

                // Покажем путь к базе
                MessageBox.Show(this, $"База данных: {dbPath}",
                    "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);

                _db = SQLiteDb.OpenOrCreate(dbPath);

                _questions = new List<Question>(_db.LoadQuestions());
                _images = new List<ImageItem>(_db.LoadImages());

                BuildQuestionRows();
                ShowCurrent();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Критическая ошибка инициализации: " + ex.Message,
                    "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }


        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            try { SafeSetImage(null); } catch { }
            try { if (_db != null) _db.Dispose(); } catch { }
        }

        // ================== ИМПОРТ ИЗОБРАЖЕНИЙ ==================
        private void Ui_btnImport_Click(object sender, EventArgs e)
        {
            if (_db == null)
            {
                MessageBox.Show(this, "База не инициализирована.", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Выберите папку с изображениями";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                string folder = dialog.SelectedPath;
                string[] exts = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff" };
                int added = 0;

                try
                {
                    var files = Directory.GetFiles(folder, "*.*", SearchOption.TopDirectoryOnly);
                    foreach (var f in files)
                    {
                        var ext = Path.GetExtension(f)?.ToLowerInvariant();
                        if (Array.IndexOf(exts, ext) < 0) continue;

                        var rel = MakeRelativePath(Path.GetDirectoryName(AppPaths.GetDefaultDbPath()), f);
                        _db.UpsertImage(rel, Path.GetFileName(f));
                        added++;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Ошибка импорта: " + ex.Message, "Ошибка",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                _images = new List<ImageItem>(_db.LoadImages());
                _index = 0;
                ShowCurrent();

                MessageBox.Show(this, $"Импортировано: {added}", "Готово",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        // ================== ВОПРОСЫ/ОТВЕТЫ ==================
        private void BuildQuestionRows()
        {
            ui_tblQuestions.SuspendLayout();

            ui_tblQuestions.Controls.Clear();
            ui_tblQuestions.RowStyles.Clear();
            ui_tblQuestions.RowCount = 0;
            _rows.Clear();

            foreach (var q in _questions.OrderBy(q => q.SortOrder).ThenBy(q => q.Id))
            {
                var lbl = new Label
                {
                    AutoSize = true,
                    Text = q.Text,
                    Margin = new Padding(6, 8, 6, 8)
                };

                var pnl = new FlowLayoutPanel
                {
                    FlowDirection = FlowDirection.LeftToRight,
                    AutoSize = true,
                    WrapContents = false,
                    Margin = new Padding(6, 4, 6, 4)
                };

                var rbYes = new RadioButton { Text = "Да", AutoSize = true, Margin = new Padding(6) };
                var rbNo = new RadioButton { Text = "Нет", AutoSize = true, Margin = new Padding(6) };
                var rbUnknown = new RadioButton { Text = "Не знаю", AutoSize = true, Margin = new Padding(6) };

                pnl.Controls.Add(rbYes);
                pnl.Controls.Add(rbNo);
                pnl.Controls.Add(rbUnknown);

                var rowIndex = ui_tblQuestions.RowCount++;
                ui_tblQuestions.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                ui_tblQuestions.Controls.Add(lbl, 0, rowIndex);
                ui_tblQuestions.Controls.Add(pnl, 1, rowIndex);

                _rows[q.Id] = new QuestionRow
                {
                    QuestionId = q.Id,
                    RbYes = rbYes,
                    RbNo = rbNo,
                    RbUnknown = rbUnknown
                };
            }

            ui_tblQuestions.ResumeLayout();
        }

        // ---- ЕДИНСТВЕННЫЙ обработчик кнопки "Сохранить и далее" ----
        private void Ui_btnSaveNext_Click(object sender, EventArgs e)
        {
            SaveAndNext();
        }

        private void Ui_btnPrev_Click(object sender, EventArgs e)
        {
            if (_images.Count == 0) return;
            if (_index > 0)
            {
                _index--;
                ShowCurrent();
            }
        }

        private void SaveAndNext()
        {
            try
            {
                if (_images.Count == 0 || _questions.Count == 0) return;

                var current = _images[_index];

                // Собрать ответы
                var answers = new Dictionary<int, AnswerValue>(_questions.Count);
                foreach (var q in _questions)
                {
                    QuestionRow row;
                    if (!_rows.TryGetValue(q.Id, out row)) continue;

                    AnswerValue val;
                    if (row.RbYes.Checked) val = AnswerValue.Yes;
                    else if (row.RbNo.Checked) val = AnswerValue.No;
                    else if (row.RbUnknown.Checked) val = AnswerValue.Unknown;
                    else
                    {
                        MessageBox.Show(this, "Не отвечены все вопросы. Отметьте каждый.", "Внимание",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    answers[q.Id] = val;
                }

                _db.SaveAnswers(current.Id, answers);

                if (_index < _images.Count - 1)
                {
                    _index++;
                    ShowCurrent();
                }
                else
                {
                    MessageBox.Show(this, "Аннотация завершена. Это было последнее изображение.", "Готово",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Ошибка сохранения: " + ex.Message, "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ================== ОТОБРАЖЕНИЕ ==================
        private void ShowCurrent()
        {
            if (_images == null || _images.Count == 0)
            {
                ui_lblProgress.Text = "0 / 0";
                ui_lblFileName.Text = "--";
                SafeSetImage(null);
                return;
            }

            if (_index < 0) _index = 0;
            if (_index >= _images.Count) _index = _images.Count - 1;

            var item = _images[_index];
            ui_lblProgress.Text = $"{_index + 1} / {_images.Count}";
            ui_lblFileName.Text = SafeShortName(item);

            ShowImage(item);

            // Подставить сохранённые ответы (если есть)
            var answers = _db.LoadAnswersForImage(item.Id);
            foreach (var q in _questions)
            {
                QuestionRow row;
                if (!_rows.TryGetValue(q.Id, out row)) continue;

                row.RbYes.Checked = false;
                row.RbNo.Checked = false;
                row.RbUnknown.Checked = false;

                AnswerValue val;
                if (answers.TryGetValue(q.Id, out val))
                {
                    switch (val)
                    {
                        case AnswerValue.Yes: row.RbYes.Checked = true; break;
                        case AnswerValue.No: row.RbNo.Checked = true; break;
                        case AnswerValue.Unknown: row.RbUnknown.Checked = true; break;
                    }
                }
            }

            ui_btnPrev.Enabled = _index > 0;
        }

        private string SafeShortName(ImageItem item)
        {
            try
            {
                var p = _db.ResolveImagePath(item.FilePath);
                return item.DisplayName ?? Path.GetFileName(p);
            }
            catch
            {
                return item.DisplayName ?? item.FilePath;
            }
        }

        private void ShowImage(ImageItem item)
        {
            try
            {
                var path = _db.ResolveImagePath(item.FilePath);
                if (!File.Exists(path))
                {
                    SafeSetImage(null);
                    MessageBox.Show(this, "Файл не найден: " + path, "Предупреждение",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var img = Image.FromStream(fs))
                {
                    var bmp = new Bitmap(img);
                    SafeSetImage(bmp);
                }
            }
            catch (Exception ex)
            {
                SafeSetImage(null);
                MessageBox.Show(this, "Ошибка загрузки изображения: " + ex.Message, "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SafeSetImage(Image newImg)
        {
            var old = ui_picture.Image;
            ui_picture.Image = newImg;
            if (old != null) old.Dispose();
        }

        // ================== УТИЛИТЫ ==================
        private static string MakeRelativePath(string baseDir, string fullPath)
        {
            if (string.IsNullOrEmpty(baseDir)) return fullPath;
            try
            {
                var baseUri = new Uri(AppendSlash(baseDir));
                var fullUri = new Uri(fullPath);
                var rel = baseUri.MakeRelativeUri(fullUri).ToString();
                return Uri.UnescapeDataString(rel.Replace('/', Path.DirectorySeparatorChar));
            }
            catch
            {
                return fullPath;
            }
        }

        private static string AppendSlash(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            var sep = Path.DirectorySeparatorChar.ToString();
            return path.EndsWith(sep) ? path : path + sep;
        }
    }
}
