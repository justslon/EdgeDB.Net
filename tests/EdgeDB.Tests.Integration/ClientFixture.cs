using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EdgeDB.Tests.Integration
{
    public class ClientFixture
    {
        public EdgeDBClient EdgeDB { get; private set; }

        public ClientFixture()
        {
            var conn = EdgeDBConnection.FromProjectFile("../../../../../edgedb.toml");

            EdgeDB = new(conn);
        }
    }
}
