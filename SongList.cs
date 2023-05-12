using System.Collections.Generic;

namespace RPG_Deck
{
    public class SongList
    {
        public List<Song> Songs { get; set; } = new List<Song>();
    }

    public class Song
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Path { get; set; }

        public Song(string name, string path)
        {
            Name = name;
            Path = path;
        }
        
        public Song() { }
    }
}