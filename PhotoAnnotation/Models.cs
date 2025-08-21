namespace PhotoAnnotation
{
    public enum AnswerValue { No = 0, Yes = 1, Unknown = 2 };

    public sealed class Question
    {
        public int Id { get; set; }
        public string Text { get; set; }
        public int SortOrder { get; set; }
    }

    public sealed class ImageItem
    {
        public int Id { get; set; }
        public string FilePath { get; set; }
        public string DisplayName { get; set; }
    }
}
