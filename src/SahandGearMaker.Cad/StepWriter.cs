using System.Globalization;
using System.Text;
using SahandGearMaker.MathX;

namespace SahandGearMaker.Cad;

/// <summary>
/// نگارندهٔ STEP AP214 (ISO 10303-21) برای جامد BREP — نسخهٔ ۰٫۵.
///
/// ساختار: PRODUCT → PRODUCT_DEFINITION → SHAPE_DEFINITION_REPRESENTATION →
/// ADVANCED_BREP_SHAPE_REPRESENTATION → MANIFOLD_SOLID_BREP → CLOSED_SHELL →
/// ADVANCED_FACE (PLANE / CYLINDRICAL_SURFACE) با مرزهای EDGE_LOOP از
/// ORIENTED_EDGE/EDGE_CURVE (LINE / CIRCLE) و VERTEX_POINT.
/// رنگ قطعه با STYLED_ITEM به کل جامد اعمال می‌شود (در بین‌کننده‌های CAD نمایش داده می‌شود).
///
/// نکات صحت:
///   - رئوس و یال‌ها بین وجوه مشترک به‌صورت سراسری یکتا می‌شوند (مدیریت منیفولد)؛
///   - کمان‌ها همیشه با دو رأس متمایز نوشته می‌شوند (بدون یال دایرهٔ کامل تک‌رأسی)؛
///   - چهارگوش‌های ناقرینه (دیوارهٔ مارپیچ) به دو مثلث مسطح شکسته می‌شوند.
/// </summary>
public sealed class StepWriter
{
    private readonly List<string> _lines = new();
    private int _nextId = 9; // ۱..۸ برای زمینهٔ محصول رزرو می‌شود

    private readonly Dictionary<string, int> _vertices = new();
    private readonly Dictionary<string, int> _edges = new();
    private readonly List<int> _faceIds = new();
    private readonly List<int> _styledItems = new();
    private string _firstSolidName = "assembly";

    public string Description { get; set; } = "Sahand Gear Maker CAD kernel export";
    public string Author { get; set; } = "Sahand Gear Maker";

    /// <summary>نوشتن یک جامد به رشتهٔ STEP.</summary>
    public string Write(CadSolid solid)
        => Write(new[] { solid });

    /// <summary>نوشتن چند جامد (مونتاژ) در یک فایل STEP.</summary>
    public string Write(IReadOnlyList<CadSolid> solids)
    {
        if (solids.Count == 0)
            throw new ArgumentException("حداقل یک جامد لازم است.", nameof(solids));
        _lines.Clear();
        _vertices.Clear();
        _edges.Clear();
        _edgeStartVertex.Clear();
        _faceIds.Clear();
        _nextId = 9;

        _firstSolidName = solids.Count == 1 ? solids[0].Name : "assembly";
        EmitHeader(_firstSolidName);

        // زمینهٔ محصول و بازنمایی
        int solidId = EmitBrep(solids);

        // ---- ساختار محصول ----
        P("#1 = APPLICATION_CONTEXT('core data for automotive mechanical design processes');");
        P("#2 = APPLICATION_PROTOCOL_DEFINITION('international standard','automotive_design',2000,#1);");
        P("#3 = PRODUCT_CONTEXT('',#1,'mechanical');");
        string pid = Esc(_firstSolidName);
        P($"#4 = PRODUCT('{pid}','{pid}','gear part',(#3));");
        P("#5 = PRODUCT_DEFINITION_FORMATION('','',#4);");
        P("#6 = PRODUCT_DEFINITION('design','',#5,#3);");
        P("#7 = PRODUCT_DEFINITION_SHAPE('','',#6);");
        P($"#8 = SHAPE_DEFINITION_REPRESENTATION(#7,#{solidId});");

        EmitFooter();
        return string.Join("\n", _lines) + "\n";
    }

    // ═══════════ ساختار فایل ═══════════

    private void EmitHeader(string name)
    {
        P("ISO-10303-21;");
        P("HEADER;");
        P($"FILE_DESCRIPTION(({Esc(Description)}),'2;1');");
        string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        P($"FILE_NAME({Esc(name + ".step")},'{now}',({Esc(Author)}),(''),'SahandGearMaker 0.5','Sahand CAD Kernel','');");
        P("FILE_SCHEMA(('AUTOMOTIVE_DESIGN { 1 0 10303 214 1 1 1 1 }'));");
        P("ENDSEC;");
        P("DATA;");
    }

    private void EmitFooter() => P("ENDSEC;\nEND-ISO-10303-21;\n");

    private void P(string line) => _lines.Add(line);

    private static string Esc(string s) => "'" + s.Replace("'", "''") + "'";

    private static string N(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    // ═══════════ هندسه پایه ═══════════

    private int Cartesian(Vector3 p)
    {
        return New($"CARTESIAN_POINT('',({N(p.X)},{N(p.Y)},{N(p.Z)}))");
    }

    private int Direction(Vector3 d)
    {
        var n = d.Normalized();
        return New($"DIRECTION('',({N(n.X)},{N(n.Y)},{N(n.Z)}))");
    }

    private int Placement(CadSurface s)
    {
        int loc = Cartesian(s.Origin);
        int axis = Direction(s.AxisZ);
        int refDir = Direction(s.AxisX);
        return New($"AXIS2_PLACEMENT_3D('',#{loc},#{axis},#{refDir})");
    }

    private int New(string body)
    {
        int id = _nextId++;
        _lines.Add($"#{id} = {body};");
        return id;
    }

    private static string VertexKey(Vector3 p)
        => $"{Z(Math.Round(p.X, 6)):R}|{Z(Math.Round(p.Y, 6)):R}|{Z(Math.Round(p.Z, 6)):R}";

    private static double Z(double v) => v == 0 ? 0 : v; // نرمال‌سازی −۰ به +۰

    private int Vertex(Vector3 p)
    {
        string key = VertexKey(p);
        if (_vertices.TryGetValue(key, out int id))
            return id;
        int cp = Cartesian(p);
        id = New($"VERTEX_POINT('',#{cp})");
        _vertices[key] = id;
        return id;
    }

    private static string EdgeKey(int v1, int v2, CadNode seg)
    {
        string curve;
        if (seg.Kind == CadSegmentKind.Line)
        {
            curve = "L";
        }
        else
        {
            // جهت‌مستقل: محور کمان به نیم‌کرهٔ کانونی می‌شود (±n همان صفحه را می‌دهد)
            var ax = CanonicalAxis(seg.ArcAxis);
            curve = $"A{VertexKey(seg.ArcCenter)}|{Z(Math.Round(ax.X, 3)):R},{Z(Math.Round(ax.Y, 3)):R},{Z(Math.Round(ax.Z, 3)):R}|{Z(Math.Round(seg.ArcRadius, 6)):R}";
        }
        int lo = Math.Min(v1, v2), hi = Math.Max(v1, v2);
        return $"{lo}|{hi}|{curve}";
    }

    private static Vector3 CanonicalAxis(Vector3 n)
    {
        n = n.Normalized();
        // اولین مؤلفهٔ غیرصفر را مثبت می‌کنیم (n و −n صفحهٔ یکسانی تعریف می‌کنند)
        if (Math.Abs(n.X) > 1e-9) return n.X > 0 ? n : -n;
        if (Math.Abs(n.Y) > 1e-9) return n.Y > 0 ? n : -n;
        return n.Z > 0 ? n : -n;
    }

    private readonly Dictionary<int, int> _edgeStartVertex = new();

    /// <summary>یال جهت‌دار؛ اگر معکوسِ یالِ ذخیره‌شده باشد orientation=.F. می‌شود.</summary>
    private (int EdgeId, bool Forward) Edge(CadNode from, CadNode to)
    {
        int v1 = Vertex(from.Point);
        int v2 = Vertex(to.Point);
        string key = EdgeKey(v1, v2, from);

        if (_edges.TryGetValue(key, out int edgeId))
            return (edgeId, _edgeStartVertex[edgeId] == v1);

        int geom = CurveGeometry(from, to);
        edgeId = New($"EDGE_CURVE('',#{v1},#{v2},#{geom},.T.);");
        _edges[key] = edgeId;
        _edgeStartVertex[edgeId] = v1;
        return (edgeId, true);
    }

    private int CurveGeometry(CadNode from, CadNode to)
    {
        if (from.Kind == CadSegmentKind.Line)
        {
            int pnt = Cartesian(from.Point);
            int dir = Direction(to.Point - from.Point);
            double len = (to.Point - from.Point).Length;
            int vec = New($"VECTOR('',#{dir},{N(len)})");
            return New($"LINE('',#{pnt},#{vec})");
        }

        // کمان: دایره در صفحهٔ کمان؛ رأس‌ها روی دایره‌اند و بین‌کننده زاویه‌ها را از رأس‌ها می‌خواند
        var axis = from.ArcAxis.Normalized();
        var refDir = Vector3.Cross(axis, from.Point - from.ArcCenter);
        if (refDir.Length < 1e-9)
            refDir = Vector3.Cross(axis, Vector3.UnitX);
        if (refDir.Length < 1e-9)
            refDir = Vector3.Cross(axis, Vector3.UnitY);
        var placement = New($"AXIS2_PLACEMENT_3D('',#{Cartesian(from.ArcCenter)},#{Direction(axis)},#{Direction(refDir.Normalized())})");
        return New($"CIRCLE('',#{placement},{N(from.ArcRadius)})");
    }

    // ═══════════ وجه‌ها و پوسته ═══════════

    private int Loop(CadLoop loop)
    {
        var oriented = new List<string>();
        int n = loop.Nodes.Count;
        for (int i = 0; i < n; i++)
        {
            var from = loop.Nodes[i];
            var to = loop.Nodes[(i + 1) % n];
            var (edgeId, forward) = Edge(from, to);
            int v1 = Vertex(from.Point);
            int v2 = Vertex(to.Point);
            var oe = New($"ORIENTED_EDGE('',#{v1},#{v2},#{edgeId},{(forward ? ".T." : ".F.")});");
            oriented.Add($"#{oe}");
        }
        return New($"EDGE_LOOP('',({string.Join(",", oriented)}))");
    }

    private int Face(CadFace f)
    {
        int surf = f.Surface switch
        {
            CadPlane p => New($"PLANE('',#{Placement(p)})"),
            CadCylinder c => New($"CYLINDRICAL_SURFACE('',#{Placement(c)},{N(c.Radius)})"),
            _ => throw new NotSupportedException("نوع سطح CAD پشتیبانی نمی‌شود."),
        };

        var bounds = new List<string>();
        int outerLoop = Loop(f.Outer);
        int ob = New($"FACE_OUTER_BOUND('',#{outerLoop},.T.);");
        bounds.Add($"#{ob}");
        foreach (var hole in f.Inner)
        {
            int hl = Loop(hole);
            int hb = New($"FACE_BOUND('',#{hl},.T.);");
            bounds.Add($"#{hb}");
        }
        int face = New($"ADVANCED_FACE('',({string.Join(",", bounds)}),#{surf},{(f.SurfaceAgreesWithOutward ? ".T." : ".F.")});");
        _faceIds.Add(face);
        return face;
    }

    private int EmitBrep(IReadOnlyList<CadSolid> solids)
    {
        var brepIds = new List<int>();
        foreach (var solid in solids)
        {
            _faceIds.Clear();
            foreach (var f in solid.Faces)
                Face(f);
            int shell = New($"CLOSED_SHELL('',({string.Join(",", _faceIds.Select(i => $"#{i}"))}))");
            int brep = New($"MANIFOLD_SOLID_BREP({Esc(solid.Name)},#{shell});");
            brepIds.Add(brep);

            // رنگ این جامد
            if (solid.Color is { } c)
            {
                int color = New($"COLOUR_RGB('',{N(Clamp01(c.R))},{N(Clamp01(c.G))},{N(Clamp01(c.B))})");
                int fasColor = New($"FILL_AREA_STYLE_COLOUR('',#{color})");
                int fill = New($"FILL_AREA_STYLE('',(#{fasColor}))");
                int surfStyle = New($"SURFACE_STYLE_FILL_AREA(#{fill})");
                int sideStyle = New($"SURFACE_SIDE_STYLE('',(#{surfStyle}))");
                int psa = New($"PRESENTATION_STYLE_ASSIGNMENT((#{sideStyle}))");
                _styledItems.Add(New($"STYLED_ITEM('',(#{psa}),#{brep})"));
            }
        }

        int rep = EmitRepresentation(brepIds);
        // رنگ‌ها باید بعد از بازنمایی اصلی بمانند (STYLED_ITEM به CTX نیاز دارد)
        return rep;
    }

    private int EmitRepresentation(List<int> brepIds)
    {

        // زمینهٔ بازنمایی هندسی (واحدها + عدم‌قطعیت)
        int lengthUnit = New("(LENGTH_UNIT()NAMED_UNIT(*)SI_UNIT(.MILLI.,.METRE.))");
        int angleUnit = New("(NAMED_UNIT(*)PLANE_ANGLE_UNIT()SI_UNIT($,.RADIAN.))");
        int solidAngleUnit = New("(NAMED_UNIT(*)SI_UNIT($,.STERADIAN.)SOLID_ANGLE_UNIT())");
        int uncertainty = New($"UNCERTAINTY_MEASURE_WITH_UNIT(LENGTH_MEASURE(1.E-07),#{lengthUnit},'distance_accuracy_value','confusion accuracy')");
        int ctx = New($"(GEOMETRIC_REPRESENTATION_CONTEXT(3)GLOBAL_UNCERTAINTY_ASSIGNED_CONTEXT((#{uncertainty}))GLOBAL_UNIT_ASSIGNED_CONTEXT((#{lengthUnit},#{angleUnit},#{solidAngleUnit}))REPRESENTATION_CONTEXT('Context #1','3D Context'))");

        string items = string.Join(",", brepIds.Select(i => $"#{i}"));
        int rep = New($"ADVANCED_BREP_SHAPE_REPRESENTATION({Esc(_firstSolidName)},({items}),#{ctx})");

        // نمایش رنگ‌ها (بازنمایی ارائهٔ هندسی)
        var styledIds = _styledItems;
        if (styledIds.Count > 0)
        {
            string styledRefs = string.Join(",", styledIds.Select(i => $"#{i}"));
            _ = New($"MECHANICAL_DESIGN_GEOMETRIC_PRESENTATION_REPRESENTATION('',({styledRefs}),#{ctx})");
        }
        return rep;
    }

    private static double Clamp01(double v) => Math.Clamp(v, 0, 1);
}
