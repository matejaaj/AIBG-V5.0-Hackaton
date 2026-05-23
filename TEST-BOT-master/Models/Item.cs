namespace SharedLibrary
{
    public class Item
    {
        public int Id;
        public string Name;
        public int? Uses;
        public string Effect;
        public int? Range;
        public int? Power;
        public int? Duration;
        public ItemType ItemType;

        public Item()
        {
            Id = 0;
            Name = "test";
            Uses = 0;
            Effect = "test";
            Range = 0;
            Power = 0;
            Duration = 0;
            ItemType = ItemType.None;
        }
        public Item(Item original)
        {
            Id = original.Id;
            Name = original.Name;
            Uses = original.Uses;
            Effect = original.Effect;
            Range = original.Range;
            Power = original.Power;
            ItemType = original.ItemType;
            Duration = original.Duration;
        }
    }
}