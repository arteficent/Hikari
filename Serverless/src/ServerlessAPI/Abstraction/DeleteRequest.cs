using ServerlessAPI.Entities;
using System.Collections.Generic;

namespace ServerlessAPI.Abstraction
{
    public class DeleteRequest
    {
        public List<Music> Items { get; set; } = new();
    }
}
