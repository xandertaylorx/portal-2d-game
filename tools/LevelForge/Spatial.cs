using System.Numerics;
using Portal2D.Core;
using Portal2D.Game;
using Portal2D.Testing;
using static Portal2D.Testing.Bot;

// Packs authored puzzles into alternating storeys. Controls retain separate circuits; persistent weights
// remain required by the final airlock. Source solutions observe the actual world through a local coordinate view.
static class Spatial
{
    public sealed record Result(LevelDef Def, int[] Goals);
    sealed record Cell(LevelDef Source, int X, int Y, bool Mirror, int GroupOffset, Int2 Spawn, Int2 Exit)
    {
        public int W => Source.Map[0].Length;
        public Vector2 Point(Vector2 p) => new(X * 32 + (Mirror ? W * 32 - p.X : p.X), Y * 32 + p.Y);
        public Vector2 Local(Vector2 p) => new(Mirror ? W * 32 - (p.X - X * 32) : p.X - X * 32, p.Y - Y * 32);
        public Vector2 Direction(Vector2 d) => new(Mirror ? -d.X : d.X, d.Y);
        public int Col(int x) => X + (Mirror ? W - 1 - x : x);
    }
    static Int2 Find(LevelDef d, char c) {
        for (int y = 0; y < d.Map.Length; y++) for (int x = 0; x < d.Map[y].Length; x++)
            if (d.Map[y][x] == c) return new(x, y);
        throw new InvalidOperationException($"{d.Name}: missing {c}");
    }
    static bool ExitRight(Cell c) => c.Source.Name == "Ascent" ? c.Mirror : c.Col(c.Exit.X) > c.X + c.W / 2;
    static bool RoofEntry(Cell c) => c.Spawn.X > 4 && c.Spawn.X < c.W - 5;
    static int LandingColumn(Cell c) {
        var map = c.Source.Map;
        return Enumerable.Range(1, c.W - 2).Where(x => map[0][x] == 'X'
            && Enumerable.Range(1, c.Spawn.Y - 1).All(y => map[y][x] == ' ')
            && map[c.Spawn.Y][x] is ' ' or 'P')
            .OrderBy(x => Math.Abs(x - c.Spawn.X)).First();
    }
    static char Flip(char c) => c switch { '>' => '<', '<' => '>', 'Q' => 'q', 'q' => 'Q', 'M' => 'N', 'N' => 'M', _ => c };
    static int Groups(LevelDef d) => 1 + d.Map.SelectMany(s => s).Select(c => Math.Max(World.ButtonGroup(c),
        Math.Max(World.ReceiverGroup(c), World.IsDoor(c) ? c - 'a' : -1))).Max();
    static char Rewire(char c, int offset) {
        int b = World.ButtonGroup(c), r = World.ReceiverGroup(c);
        if (b >= 0) return World.IsTimedButton(c) ? World.TimedButtonChar(b + offset) : World.ButtonChar(b + offset);
        if (r >= 0) return World.ReceiverChar(r + offset);
        return World.IsDoor(c) ? World.DoorChar(c - 'a' + offset) : c;
    }

    public static Result Build(string name, LevelDef[] sources, int theme = 1, bool horizontal = false)
    {
        if (sources.Any(d => d.Gun != GunMode.Both || d.Map.Any(r => r.Contains('u') || r.Contains('o'))))
            throw new InvalidOperationException("Fixed portals need a standalone chamber");
        if (sources.Sum(Groups) > World.Groups) throw new InvalidOperationException("More than six circuits");
        int width = horizontal ? sources.Sum(d => d.Map[0].Length + 5) + 4 : sources.Max(d => d.Map[0].Length) + 10;
        int height = horizontal ? sources.Max(d => d.Map.Length) + 4 : sources.Sum(d => d.Map.Length + 4) + 2;
        var b = new LevelBuilder(width, height);
        var cells = new List<Cell>();
        bool enterRight = false; int row = 1, col = 4, group = 0;
        var goals = new List<int>();
        for (int i = 0; i < sources.Length; i++) {
            var s = sources[i]; var spawn = Find(s, 'P'); var exit = Find(s, 'E');
            bool mirror = (spawn.X > s.Map[0].Length / 2) != enterRight;
            int x = horizontal ? col : (width - s.Map[0].Length) / 2;
            var c = new Cell(s, x, row, mirror, group, spawn, exit); cells.Add(c);
            for (int y = 0; y < s.Map.Length; y++) for (int xx = 0; xx < s.Map[y].Length; xx++) {
                char tile = s.Map[y][xx];
                if (tile == 'P' && i > 0 || tile == 'E' && i < sources.Length - 1) tile = ' ';
                b.Set(c.Col(xx), y + row, Rewire(mirror ? Flip(tile) : tile, group));
            }
            // A weight remains meaningful after leaving its floor. Portal-powered receivers only latch the local route.
            for (int g = 0; g < Groups(s); g++) {
                var controls = s.Map.SelectMany(r => r).Where(t => World.ButtonGroup(t) == g || World.ReceiverGroup(t) == g).ToArray();
                if (controls.Length > 0 && controls.All(t => !World.IsTimedButton(t))
                    && (controls.All(t => World.ButtonGroup(t) >= 0) || !s.Map.Any(r => r.Contains('#')))) goals.Add(group + g);
            }
            group += Groups(s);
            enterRight = ExitRight(c);
            if (horizontal) { col += s.Map[0].Length + 5; enterRight = false; }
            else row += s.Map.Length + 4;
        }
        for (int i = 0; i < cells.Count - 1; i++) {
            var a = cells[i]; var n = cells[i + 1];
            int exitX = a.Col(a.Exit.X), exitY = a.Y + a.Exit.Y;
            int spawnX = n.Col(n.Spawn.X), spawnY = n.Y + n.Spawn.Y;
            int shaft = horizontal ? a.X + a.W + 2 : ExitRight(a) ? width - 3 : 2;
            Passage(exitX, shaft, exitY, a);
            if (!horizontal && RoofEntry(n)) {
                int roof = n.Y - 2, landing = n.Col(LandingColumn(n));
                b.Fill(shaft - 1, exitY - 1, shaft + 1, roof, ' ');
                b.Fill(shaft - 1, roof + 1, shaft + 1, roof + 1, 'X');
                b.Fill(Math.Min(shaft, landing), roof - 1, Math.Max(shaft, landing), roof, ' ');
                b.Fill(Math.Min(shaft, landing), roof + 1, Math.Max(shaft, landing), roof + 1, 'X');
                b.Fill(landing, roof, landing, n.Y, '|');
            } else {
                b.Fill(shaft - 1, Math.Min(exitY, spawnY) - 1, shaft + 1, Math.Max(exitY, spawnY), ' ');
                b.Fill(shaft - 1, Math.Max(exitY, spawnY) + 1, shaft + 1, Math.Max(exitY, spawnY) + 1, 'X');
                int entryWall = n.Mirror ? n.X + n.W - 1 : n.X;
                if (n.Source.Map[n.Spawn.Y][n.Mirror ? n.W - 1 : 0] == '#') {
                    // Enter above an indispensable wall portal, preserving both portal tiles.
                    Passage(shaft, entryWall, spawnY, n);
                    for (int yy = spawnY - 1; yy <= spawnY; yy++) b.Set(entryWall, yy, '#');
                    b.Fill(entryWall, spawnY - 3, entryWall, spawnY - 2, '|');
                    b.Fill(entryWall - 3, spawnY - 4, entryWall - 1, spawnY, ' ');
                } else {
                    Passage(shaft, spawnX, spawnY, n);
                    b.Fill(entryWall, spawnY - 1, entryWall, spawnY, '|');
                }
            }
        }
        void Passage(int from, int to, int y, Cell owner) {
            for (int x = Math.Min(from, to); x <= Math.Max(from, to); x++) {
                for (int yy = y - 1; yy <= y; yy++) {
                    char tile = b.Get(x, yy);
                    if (tile is 'X' or '#' or '=' or ' ') b.Set(x, yy, ' ');
                }
                // Preserve portal floors and other mechanics inside the source room.
                if (x <= owner.X || x >= owner.X + owner.W - 1 || b.Get(x, y + 1) is ' ' or '~') b.Set(x, y + 1, 'X');
            }
        }
        var def = new LevelDef { Name = name, Map = b.Build(), Theme = theme, Fling = sources.Any(s => s.Fling),
            Hints = ["Leave each weight circuit powered as you move through the facility.", "The final airlock checks the completed circuits."],
            Solution = bot => Solve(bot, cells, width, horizontal) };
        return new(def, goals.ToArray());
    }

    static IEnumerable<InputState> Solve(Bot actual, List<Cell> cells, int width, bool horizontal)
    {
        for (int index = 0; index < cells.Count; index++) {
            var cell = cells[index];
            if (index > 0) {
                var prev = cells[index - 1];
                int exitX = prev.Col(prev.Exit.X);
                int shaft = horizontal ? prev.X + prev.W + 2 : ExitRight(prev) ? width - 3 : 2;
                foreach (var input in actual.WalkTo(X(shaft), timeout: 20)) yield return input;
                if (!horizontal && RoofEntry(cell)) {
                    foreach (var input in actual.WalkTo(X(cell.Col(LandingColumn(cell))), timeout: 20)) yield return input;
                    foreach (var input in actual.Until(() => actual.Player.OnGround && actual.Player.Pos.Y > (cell.Y + cell.Spawn.Y - 1) * 32, 8)) yield return input;
                }
                foreach (var input in actual.WalkTo(X(cell.Col(cell.Spawn.X)), timeout: 20)) yield return input;
                foreach (var input in actual.Wait(0.4f)) yield return input;
            }
            var local = new World(cell.Source); var proxy = new Bot(local);
            var player = local.Player!;
            int teleports = actual.Player.Teleports;
            var cubes = local.Cubes.Select(c => actual.W.Cubes.Single(real => Vector2.Distance(real.Spawn, cell.Point(c.Spawn)) < 0.1f)).ToArray();
            void Sync() {
                var p = actual.Player;
                player.Pos = cell.Local(p.Pos); player.Vel = cell.Direction(p.Vel); player.OnGround = p.OnGround;
                player.Teleports = p.Teleports - teleports; player.Dead = p.Dead; player.Won = false;
                player.Holding = null; player.Facing = cell.Mirror ? -p.Facing : p.Facing;
                for (int j = 0; j < cubes.Length; j++) {
                    var from = cubes[j]; var to = local.Cubes[j];
                    to.Pos = cell.Local(from.Pos); to.Vel = cell.Direction(from.Vel); to.OnGround = from.OnGround;
                    to.Held = from.Held; to.Gone = from.Gone; to.Dissolving = from.Dissolving;
                    if (p.Holding == from) player.Holding = to;
                }
                for (int g = 0; g < Groups(cell.Source); g++) {
                    local.DoorOpen[g] = actual.W.DoorOpen[g + cell.GroupOffset];
                    local.GroupActive[g] = actual.W.GroupActive[g + cell.GroupOffset];
                }
            }
            Sync();
            using var sequence = cell.Source.Solution!(proxy).GetEnumerator();
            while (!actual.Player.Dead && !actual.Player.Won) {
                Sync();
                if (!sequence.MoveNext()) break;
                var input = sequence.Current;
                var aim = input.AimWorld;
                if (cell.Mirror && (input.FireBlue || input.FireOrange)) {
                    // Pair selection is left-biased for floor/ceiling portals. Aim inside the reflected pair.
                    var direction = Vector2.Normalize(aim - World.GunOrigin(player));
                    var preview = local.PreviewShot(World.GunOrigin(player), direction, input.FireBlue ? PortalColor.Blue : PortalColor.Orange);
                    if (preview.candidate is { } candidate && !candidate.IsWall)
                        aim = candidate.Center + new Vector2(
                            candidate.Normal.Y > 0 && cell.Source.Map.SelectMany(r => r).Any(World.IsReceiver) ? -24 : 8, 0)
                            - candidate.Normal * 0.5f;
                }
                input.AimWorld = cell.Point(aim); input.MoveX *= cell.Mirror ? -1 : 1;
                yield return input;
            }
            var end = cell.Local(actual.Player.Pos);
            if (!actual.Player.Won && (Math.Abs(end.X - X(cell.Exit.X)) > 48 || Math.Abs(end.Y - ((cell.Exit.Y + 1) * 32 - 15)) > 64))
                throw new InvalidOperationException($"{cell.Source.Name}: stage did not finish at its exit ({end})");
        }
    }
}
