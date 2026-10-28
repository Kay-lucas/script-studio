using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;

namespace ScriptStudio {
    [DataContract]
    public class Recording {
        [DataMember] public string Id = Guid.NewGuid().ToString("N");
        [DataMember] public string Path;
        [DataMember] public long Size;
        [DataMember] public long Modified;
        [DataMember] public string Status = "queued";
        [DataMember] public string Text = "";
        [DataMember] public string Error = "";
        [DataMember] public string Model = "";
        [DataMember] public bool Checked = true;
        public string Name { get { return System.IO.Path.GetFileName(Path); } }
    }
    public class ModelSpec {
        public string Id, Label, Sha1;
        public int MB;
        public ModelSpec(string id, string label, int mb, string sha) { Id=id;Label=label;MB=mb;Sha1=sha; }
        public override string ToString() { return Label; }
    }
    public static class Core {
        public static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
        public static readonly string Data = System.IO.Path.Combine(Root,"data");
        public static readonly string Results = System.IO.Path.Combine(Data,"results");
        public static readonly string Engine = System.IO.Path.Combine(Root,"runtime","engine","Release","whisper-cli.exe");
        public static readonly ModelSpec[] Models = {
            new ModelSpec("base.en","기본  ·  base.en  ·  약 150MB",150,"137c40403d78fd54d454da0f9bd998f78703390c"),
            new ModelSpec("small.en","정확도 우선  ·  small.en  ·  약 490MB",490,"db8a495a91d927739e50b3fc1cc4c6b8f6c2d022"),
            new ModelSpec("tiny.en","빠른 초안  ·  tiny.en  ·  약 80MB",80,"c78c86eb1a8faa21b369bcd33207cc90d64ae9df")
        };
        public static string ModelPath(ModelSpec m) { return System.IO.Path.Combine(Root,"models","ggml-"+m.Id+".bin"); }
        public static bool Supported(string path) { return new[]{".mp3",".wav",".flac",".ogg"}.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant()); }
        public static string SortKey(string s) { return Regex.Replace(s.ToLowerInvariant(),@"\d+",m=>m.Value.PadLeft(18,'0')); }
        public static string Quote(string s) {
            var b=new StringBuilder("\"");int slashes=0;
            foreach(char c in s){
                if(c=='\\'){slashes++;continue;}
                if(c=='\"')b.Append('\\',slashes*2+1).Append(c);
                else b.Append('\\',slashes).Append(c);
                slashes=0;
            }
            return b.Append('\\',slashes*2).Append('"').ToString();
        }
        public static void AtomicWrite(string path,string content) {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            string tmp=path+".tmp";
            File.WriteAllText(tmp,content,new UTF8Encoding(false));
            if(File.Exists(path)) File.Replace(tmp,path,null); else File.Move(tmp,path);
        }
        public static void SaveSession(List<Recording> records) {
            Directory.CreateDirectory(Results);
            string path=System.IO.Path.Combine(Data,"session.json"),tmp=path+".tmp";
            using(var f=File.Create(tmp)) new DataContractJsonSerializer(typeof(List<Recording>)).WriteObject(f,records);
            if(File.Exists(path)) File.Replace(tmp,path,null);else File.Move(tmp,path);
        }
        public static List<Recording> LoadSession() {
            string path=System.IO.Path.Combine(Data,"session.json");
            if(!File.Exists(path)) return new List<Recording>();
            try {
                using(var f=File.OpenRead(path)) {
                    var list=(List<Recording>)new DataContractJsonSerializer(typeof(List<Recording>)).ReadObject(f);
                    foreach(var r in list) {
                        if(r.Status=="running") r.Status="queued";if(r.Text==null)r.Text="";
                        var source=new FileInfo(r.Path);
                        if(source.Exists&&(source.Length!=r.Size||source.LastWriteTimeUtc.Ticks!=r.Modified)) {r.Size=source.Length;r.Modified=source.LastWriteTimeUtc.Ticks;r.Status="queued";}
                    }
                    return list;
                }
            } catch {
                string copy=path+".unreadable-"+DateTime.Now.ToString("yyyyMMddHHmmss");
                File.Copy(path,copy,true);
                throw new InvalidDataException("이전 작업 목록을 읽지 못했습니다. data 폴더에 원본을 보관했습니다.");
            }
        }
        public static void SaveTranscript(Recording r) {
            AtomicWrite(System.IO.Path.Combine(Results,r.Id+".txt"),r.Text);
        }
        public static string ReadUtf8(string path) { return File.ReadAllText(path,Encoding.UTF8).Trim(); }
        public static void CheckRuntime() {
            var needed=new[]{"MSVCP140.dll","VCRUNTIME140.dll","VCRUNTIME140_1.dll","VCOMP140.DLL"};
            var missing=needed.Where(name=>!File.Exists(System.IO.Path.Combine(Environment.SystemDirectory,name))&&!File.Exists(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Engine),name))).ToArray();
            if(missing.Length>0)throw new InvalidOperationException("이 PC에는 음성 인식 도구에 필요한 Microsoft Visual C++ x64 실행 구성 요소가 없습니다.\r\n앱의 사용안내.txt에 있는 Microsoft 공식 링크에서 설치한 뒤 다시 실행해 주세요.\r\n누락된 파일: "+string.Join(", ",missing));
        }
        public static async Task<string> Transcribe(string input,ModelSpec model,CancellationToken token,Action<string> progress) {
            CheckRuntime();
            if(!File.Exists(Engine)) throw new FileNotFoundException("runtime 폴더에서 음성 인식 도구를 찾을 수 없습니다. 앱 압축을 모두 풀어 주세요.");
            if(!File.Exists(ModelPath(model)))throw new FileNotFoundException("선택한 모델을 먼저 준비해 주세요.");
            if(!File.Exists(input))throw new FileNotFoundException("원본 음원 파일을 찾을 수 없습니다.");
            Directory.CreateDirectory(System.IO.Path.Combine(Data,"temp"));
            string relative=System.IO.Path.Combine("data","temp",Guid.NewGuid().ToString("N"));
            string prefix=System.IO.Path.Combine(Root,relative),inputRelative=relative+System.IO.Path.GetExtension(input).ToLowerInvariant();
            string inputCopy=System.IO.Path.Combine(Root,inputRelative);
            // Narrow native argv cannot reliably represent every Windows filename.
            // Keep native arguments ASCII and use a temporary copy for one recording.
            var info=new ProcessStartInfo(Engine,"-m "+Quote(System.IO.Path.Combine("models","ggml-"+model.Id+".bin"))+" -f "+Quote(inputRelative)+" -l en -ng -t "+Math.Min(8,Math.Max(1,Environment.ProcessorCount-1))+" -otxt -nt -pp -of "+Quote(relative));
            info.UseShellExecute=false;info.CreateNoWindow=true;info.RedirectStandardOutput=true;info.RedirectStandardError=true;
            info.WorkingDirectory=Root;
            info.StandardOutputEncoding=Encoding.UTF8;info.StandardErrorEncoding=Encoding.UTF8;
            var errors=new StringBuilder();
            using(var p=new Process()) {
                p.StartInfo=info;
                p.OutputDataReceived+=(s,e)=>{};
                p.ErrorDataReceived+=(s,e)=>{
                    if(e.Data==null)return;
                    lock(errors) { if(errors.Length<24000) errors.AppendLine(e.Data); }
                    var match=Regex.Match(e.Data,@"progress\s*=\s*(\d+)%");
                    if(match.Success&&progress!=null)progress(match.Groups[1].Value+"%");
                };
                try {
                    token.ThrowIfCancellationRequested();await Task.Run(()=>File.Copy(input,inputCopy));
                    token.ThrowIfCancellationRequested();p.Start();p.BeginOutputReadLine();p.BeginErrorReadLine();
                    using(token.Register(()=>{try{if(!p.HasExited)p.Kill();}catch(InvalidOperationException){} })) {
                        await Task.Run(()=>p.WaitForExit());
                    }
                    token.ThrowIfCancellationRequested();
                    if(p.ExitCode!=0||!File.Exists(prefix+".txt")) {
                        if(p.ExitCode==unchecked((int)0xC0000135)||p.ExitCode==unchecked((int)0xC000007B))throw new InvalidOperationException("음성 인식 도구를 시작하지 못했습니다. Microsoft Visual C++ x64 실행 구성 요소를 설치하거나 업데이트해 주세요. 사용안내.txt에 공식 링크가 있습니다.");
                        string err=errors.ToString();
                        if(err.Length>1800)err=err.Substring(err.Length-1800);
                        throw new InvalidOperationException("음성 변환에 실패했습니다. 파일과 모델을 확인해 주세요.\r\n"+err);
                    }
                    string text=ReadUtf8(prefix+".txt");
                    if(string.IsNullOrWhiteSpace(text))throw new InvalidOperationException("인식된 음성이 없습니다. 파일을 재생해 확인해 주세요.");
                    return text;
                } finally { if(File.Exists(prefix+".txt"))File.Delete(prefix+".txt");if(File.Exists(inputCopy))File.Delete(inputCopy); }
            }
        }
        public static async Task DownloadModel(ModelSpec model,IProgress<int> progress,CancellationToken token) {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ModelPath(model)));
            string path=ModelPath(model),part=path+".part";
            ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
            try {
                using(var client=new HttpClient()) {
                    client.Timeout=TimeSpan.FromMinutes(60);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("ScriptStudio/1.0");
                    using(var response=await client.GetAsync("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-"+model.Id+".bin?download=true",HttpCompletionOption.ResponseHeadersRead,token)) {
                        response.EnsureSuccessStatusCode();
                        long total=response.Content.Headers.ContentLength??(model.MB*1000000L),done=0;
                        using(var src=await response.Content.ReadAsStreamAsync())
                        using(var dst=new FileStream(part,FileMode.Create,FileAccess.Write,FileShare.None,81920,true)) {
                            byte[] buf=new byte[81920];int n;
                            while((n=await src.ReadAsync(buf,0,buf.Length,token))>0) {
                                await dst.WriteAsync(buf,0,n,token);done+=n;
                                progress.Report((int)Math.Min(99,done*100/Math.Max(1,total)));
                            }
                        }
                    }
                }
                string actual=await Task.Run(()=>{using(var f=File.OpenRead(part))using(var sha=SHA1.Create())return BitConverter.ToString(sha.ComputeHash(f)).Replace("-","").ToLowerInvariant();});
                token.ThrowIfCancellationRequested();
                if(actual!=model.Sha1)throw new InvalidDataException("모델 파일 검증에 실패했습니다. 다시 내려받아 주세요.");
                if(File.Exists(path))File.Replace(part,path,null);else File.Move(part,path);
                progress.Report(100);
            } finally { if(File.Exists(part))File.Delete(part); }
        }
        public static void ExportText(string path,IEnumerable<Recording> records) {
            File.WriteAllText(path,string.Join("\r\n\r\n",records.Select(r=>System.IO.Path.GetFileNameWithoutExtension(r.Path)+"\r\n\r\n"+r.Text)),new UTF8Encoding(true));
        }
        static void Entry(ZipArchive zip,string path,string xml) {
            using(var s=zip.CreateEntry(path).Open())using(var w=new StreamWriter(s,new UTF8Encoding(false)))w.Write(xml);
        }
        public static void ExportWord(string path,IEnumerable<Recording> records) {
            string tmp=path+".tmp";
            try {
                using(var f=File.Create(tmp))using(var zip=new ZipArchive(f,ZipArchiveMode.Create)) {
                    Entry(zip,"[Content_Types].xml","<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/><Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/></Types>");
                    Entry(zip,"_rels/.rels","<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
                    Entry(zip,"word/_rels/document.xml.rels","<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
                    Entry(zip,"word/styles.xml","<?xml version=\"1.0\"?><w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\" w:eastAsia=\"Malgun Gothic\"/><w:sz w:val=\"24\"/><w:color w:val=\"000000\"/></w:rPr></w:rPrDefault><w:pPrDefault><w:pPr><w:spacing w:after=\"160\" w:line=\"300\" w:lineRule=\"auto\"/></w:pPr></w:pPrDefault></w:docDefaults><w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style><w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:keepNext/><w:spacing w:after=\"300\"/><w:outlineLvl w:val=\"0\"/></w:pPr><w:rPr><w:b/><w:sz w:val=\"36\"/></w:rPr></w:style></w:styles>");
                    var e=zip.CreateEntry("word/document.xml");
                    using(var s=e.Open())using(var x=XmlWriter.Create(s,new XmlWriterSettings{Encoding=new UTF8Encoding(false),Indent=false})) {
                        const string ns="http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                        x.WriteStartDocument();x.WriteStartElement("w","document",ns);x.WriteStartElement("w","body",ns);
                        bool first=true;
                        foreach(var r in records) {
                            if(!first){x.WriteStartElement("w","p",ns);x.WriteStartElement("w","r",ns);x.WriteStartElement("w","br",ns);x.WriteAttributeString("w","type",ns,"page");x.WriteEndElement();x.WriteEndElement();x.WriteEndElement();}first=false;
                            Paragraph(x,System.IO.Path.GetFileNameWithoutExtension(r.Path),true);
                            foreach(string line in r.Text.Replace("\r\n","\n").Replace('\r','\n').Split('\n'))Paragraph(x,line,false);
                        }
                        x.WriteStartElement("w","sectPr",ns);
                        x.WriteStartElement("w","pgSz",ns);x.WriteAttributeString("w","w",ns,"11906");x.WriteAttributeString("w","h",ns,"16838");x.WriteEndElement();
                        x.WriteStartElement("w","pgMar",ns);foreach(string a in new[]{"top","right","bottom","left"})x.WriteAttributeString("w",a,ns,"1134");x.WriteEndElement();
                        x.WriteEndElement();x.WriteEndElement();x.WriteEndElement();x.WriteEndDocument();
                    }
                }
                if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);
            }finally { if(File.Exists(tmp))File.Delete(tmp); }
        }
        static void Paragraph(XmlWriter x,string text,bool heading) {
            const string ns="http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            x.WriteStartElement("w","p",ns);
            if(heading){x.WriteStartElement("w","pPr",ns);x.WriteStartElement("w","pStyle",ns);x.WriteAttributeString("w","val",ns,"Heading1");x.WriteEndElement();x.WriteEndElement();}
            x.WriteStartElement("w","r",ns);x.WriteStartElement("w","t",ns);x.WriteAttributeString("xml","space",null,"preserve");
            x.WriteString(new string(text.Where(c=>XmlConvert.IsXmlChar(c)).ToArray()));
            x.WriteEndElement();x.WriteEndElement();x.WriteEndElement();
        }
    }

    public class StudioForm : Form {
        readonly Color Ink=Color.FromArgb(27,42,59), Muted=Color.FromArgb(103,114,125), Teal=Color.FromArgb(20,107,101), Paper=Color.FromArgb(245,247,246);
        List<Recording> records=new List<Recording>();
        ListView files=new ListView();
        TextBox editor=new TextBox();
        Label detail=new Label(),summary=new Label(),status=new Label(),modelState=new Label();
        ComboBox models=new ComboBox();
        Button add,folder,remove,run,stop,prepare,saveWord,saveText,play,clear;
        CheckBox all=new CheckBox();
        ProgressBar progress=new ProgressBar();
        System.Windows.Forms.Timer saveTimer=new System.Windows.Forms.Timer();
        CancellationTokenSource cancellation;
        Recording current;
        bool loading=false,busy=false,dirty=false;
        string busyKind="";
        public StudioForm(bool loadSession) {
            Text="Script Studio  ·  영어 음원 스크립트";
            Font=new Font("맑은 고딕",10F);BackColor=Paper;ForeColor=Ink;
            ClientSize=new Size(1100,800);MinimumSize=new Size(980,740);StartPosition=FormStartPosition.CenterScreen;
            AutoScaleMode=AutoScaleMode.Dpi;AllowDrop=true;
            Build();
            if(loadSession) {
                try { records=Core.LoadSession(); }catch(Exception ex){MessageBox.Show(this,ex.Message,"작업 복원 안내");}
            }
            RefreshList();UpdateModel();
            saveTimer.Interval=650;
            saveTimer.Tick+=(s,e)=>{saveTimer.Stop();SafeSave();};
            DragEnter+=(s,e)=>{if(e.Data.GetDataPresent(DataFormats.FileDrop)&&!busy)e.Effect=DragDropEffects.Copy;};
            DragDrop+=(s,e)=>{if(!busy)AddPaths((string[])e.Data.GetData(DataFormats.FileDrop));};
            FormClosing+=OnClosing;
        }
        Button Button(string text,int width,EventHandler handler,bool primary=false) {
            var b=new Button{Text=text,Width=width,Height=36,FlatStyle=FlatStyle.Flat,BackColor=primary?Teal:Color.White,ForeColor=primary?Color.White:Ink,Margin=new Padding(0,0,8,0),Cursor=Cursors.Hand};
            b.FlatAppearance.BorderColor=primary?Teal:Color.FromArgb(212,219,219);b.Click+=handler;return b;
        }
        Label Label(string text,float size,bool bold=false) { return new Label{Text=text,AutoSize=true,Font=new Font("맑은 고딕",size,bold?FontStyle.Bold:FontStyle.Regular),ForeColor=Ink,Margin=new Padding(0,0,0,8)}; }
        void Build() {
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Padding=new Padding(26,20,26,18)};
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,115));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,81));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,60));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,45));Controls.Add(layout);
            var header=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false};
            header.Controls.Add(Label("SCRIPT STUDIO",11,true));header.Controls.Add(Label("영어 음원을 읽을 수 있는 스크립트로",23,true));
            var sub=Label("무료 오프라인 변환  ·  파일은 이 PC에서 처리됩니다",10);sub.ForeColor=Muted;header.Controls.Add(sub);layout.Controls.Add(header,0,0);
            var modelPanel=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=2,BackColor=Color.White,Padding=new Padding(12,8,12,8)};
            modelPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,72));modelPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));modelPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,143));
            var modelLabel=Label("인식 모델",10,true);modelLabel.Anchor=AnchorStyles.Left;modelPanel.Controls.Add(modelLabel,0,0);
            models.DropDownStyle=ComboBoxStyle.DropDownList;models.Dock=DockStyle.Fill;models.Items.AddRange(Core.Models);models.SelectedIndex=0;models.SelectedIndexChanged+=(s,e)=>UpdateModel();
            models.DrawMode=DrawMode.OwnerDrawFixed;models.ItemHeight=24;models.DrawItem+=(s,e)=>{e.DrawBackground();if(e.Index>=0)TextRenderer.DrawText(e.Graphics,models.Items[e.Index].ToString(),models.Font,e.Bounds,Ink,TextFormatFlags.VerticalCenter|TextFormatFlags.Left);e.DrawFocusRectangle();};modelPanel.Controls.Add(models,1,0);
            prepare=Button("선택 모델 내려받기",140,async(s,e)=>await PrepareModel());prepare.Height=30;modelPanel.Controls.Add(prepare,2,0);
            modelState.AutoSize=true;modelState.ForeColor=Muted;modelState.Font=new Font(Font.FontFamily,9F);modelState.Margin=new Padding(0,4,0,0);modelPanel.Controls.Add(modelState,1,1);modelPanel.SetColumnSpan(modelState,2);layout.Controls.Add(modelPanel,0,1);
            var split=new SplitContainer{Size=new Size(1048,400),Dock=DockStyle.Fill,SplitterDistance=410,FixedPanel=FixedPanel.Panel1,Panel1MinSize=300,Panel2MinSize=350,BackColor=Paper,SplitterWidth=18,Margin=new Padding(0,16,0,4)};
            layout.Controls.Add(split,0,2);
            var left=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4};left.RowStyles.Add(new RowStyle(SizeType.Absolute,32));left.RowStyles.Add(new RowStyle(SizeType.Absolute,46));left.RowStyles.Add(new RowStyle(SizeType.Percent,100));left.RowStyles.Add(new RowStyle(SizeType.Absolute,30));split.Panel1.Controls.Add(left);
            left.Controls.Add(Label("01  음원 파일",12,true),0,0);
            var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};
            add=Button("파일 추가",102,(s,e)=>ChooseFiles());folder=Button("폴더 추가",102,(s,e)=>ChooseFolder());remove=Button("선택 항목 빼기",130,(s,e)=>RemoveSelected());buttons.Controls.AddRange(new Control[]{add,folder,remove});left.Controls.Add(buttons,0,1);
            files.Dock=DockStyle.Fill;files.View=View.Details;files.FullRowSelect=true;files.CheckBoxes=true;files.HideSelection=false;files.MultiSelect=true;files.BorderStyle=BorderStyle.FixedSingle;files.BackColor=Color.White;
            files.Columns.Add("파일 이름",220);files.Columns.Add("상태",105);files.Columns.Add("MB",57,HorizontalAlignment.Right);
            files.SelectedIndexChanged+=(s,e)=>SelectRecording();
            files.ItemChecked+=(s,e)=>{if(!loading){((Recording)e.Item.Tag).Checked=e.Item.Checked;UpdateSummary();ScheduleSave();}};
            files.Resize+=(s,e)=>{if(files.ClientSize.Width>200)files.Columns[0].Width=Math.Max(130,files.ClientSize.Width-172);};left.Controls.Add(files,0,2);
            var foot=new FlowLayoutPanel{Dock=DockStyle.Fill};all.Text="전체 선택";all.AutoSize=true;all.Margin=new Padding(0,4,12,0);
            all.CheckedChanged+=(s,e)=>{if(!loading){loading=true;foreach(ListViewItem item in files.Items){item.Checked=all.Checked;((Recording)item.Tag).Checked=all.Checked;}loading=false;UpdateSummary();ScheduleSave();}};
            foot.Controls.Add(all);var hint=Label("파일을 이 창에 끌어놓아도 됩니다",9);hint.Margin=new Padding(0,6,0,0);hint.ForeColor=Muted;foot.Controls.Add(hint);left.Controls.Add(foot,0,3);
            var right=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5};right.RowStyles.Add(new RowStyle(SizeType.Absolute,32));right.RowStyles.Add(new RowStyle(SizeType.Absolute,46));right.RowStyles.Add(new RowStyle(SizeType.Percent,100));right.RowStyles.Add(new RowStyle(SizeType.Absolute,30));right.RowStyles.Add(new RowStyle(SizeType.Absolute,42));split.Panel2.Controls.Add(right);
            right.Controls.Add(Label("02  스크립트 확인과 편집",12,true),0,0);detail.Dock=DockStyle.Fill;detail.ForeColor=Muted;detail.Text="목록에서 파일을 선택하세요";detail.AutoEllipsis=true;detail.Padding=new Padding(0,5,0,0);right.Controls.Add(detail,0,1);
            editor.Dock=DockStyle.Fill;editor.Multiline=true;editor.ScrollBars=ScrollBars.Vertical;editor.Font=new Font("Segoe UI",12F);editor.BackColor=Color.White;editor.BorderStyle=BorderStyle.FixedSingle;editor.ReadOnly=true;editor.MaxLength=0;
            editor.TextChanged+=(s,e)=>{if(!loading&&current!=null){current.Text=editor.Text;dirty=true;ScheduleSave();UpdateSummary();}};right.Controls.Add(editor,0,2);
            var saved=Label("수정 내용과 완료된 작업은 자동으로 저장됩니다",9);saved.ForeColor=Muted;saved.Margin=new Padding(0,7,0,0);right.Controls.Add(saved,0,3);
            var editButtons=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};play=Button("원본 재생",100,(s,e)=>Play());var copy=Button("복사",72,(s,e)=>{try{if(editor.TextLength>0)Clipboard.SetText(editor.Text);}catch(Exception ex){MessageBox.Show(this,ex.Message,"복사 오류");}});clear=Button("이 파일 다시 변환",151,(s,e)=>Requeue());editButtons.Controls.AddRange(new Control[]{play,copy,clear});right.Controls.Add(editButtons,0,4);
            var action=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,Padding=new Padding(0,12,0,0)};action.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));action.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            var actionLeft=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};run=Button("선택 파일 변환",144,async(s,e)=>await RunQueue(),true);stop=Button("중단",70,(s,e)=>{if(cancellation!=null)cancellation.Cancel();status.Text="작업을 중단하고 있습니다…";});stop.Enabled=false;actionLeft.Controls.AddRange(new Control[]{run,stop});summary.AutoSize=true;summary.Padding=new Padding(5,8,0,0);summary.ForeColor=Muted;actionLeft.Controls.Add(summary);action.Controls.Add(actionLeft,0,0);
            var actionRight=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false};saveWord=Button("선택 스크립트 Word 저장",221,(s,e)=>Export(true),true);saveText=Button("텍스트 저장",120,(s,e)=>Export(false));actionRight.Controls.AddRange(new Control[]{saveWord,saveText});action.Controls.Add(actionRight,1,0);layout.Controls.Add(action,0,3);
            var bottom=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,Padding=new Padding(0,8,0,0)};bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,190));status.Dock=DockStyle.Fill;status.AutoEllipsis=true;status.ForeColor=Muted;status.Text="MP3 파일을 추가해 주세요. 완료된 결과는 앱을 다시 열어도 남아 있습니다.";bottom.Controls.Add(status,0,0);progress.Dock=DockStyle.Fill;progress.Height=16;progress.Margin=new Padding(8,2,0,10);bottom.Controls.Add(progress,1,0);layout.Controls.Add(bottom,0,4);
        }
        void UpdateModel() {
            if(modelState==null||models.SelectedItem==null)return;
            var m=(ModelSpec)models.SelectedItem;
            bool ready=File.Exists(Core.ModelPath(m));
            modelState.Text=ready?"준비 완료  ·  인터넷 연결 없이 변환할 수 있습니다":"최초 1회 약 "+m.MB+"MB 다운로드가 필요합니다  ·  이후 오프라인 사용";
            if(prepare!=null)prepare.Enabled=!busy&&!ready;
        }
        string State(Recording r) { switch(r.Status){case "done":return "완료";case "running":return "변환 중";case "error":return "오류";default:return "대기";} }
        void RefreshList() {
            loading=true;files.BeginUpdate();files.Items.Clear();
            foreach(var r in records.OrderBy(r=>Core.SortKey(r.Name))) {
                var item=new ListViewItem(r.Name){Tag=r,Checked=r.Checked};item.SubItems.Add(State(r));item.SubItems.Add((r.Size/1000000.0).ToString("0.0"));item.ToolTipText=r.Path;files.Items.Add(item);
            }
            files.EndUpdate();loading=false;UpdateSummary();
            if(current!=null){var item=files.Items.Cast<ListViewItem>().FirstOrDefault(i=>i.Tag==current);if(item!=null)item.Selected=true;else{current=null;ShowCurrent();}}
        }
        void UpdateSummary() {
            int selected=records.Count(r=>r.Checked),done=records.Count(r=>r.Status=="done");
            summary.Text=selected+"개 선택 · "+done+"개 완료";
            saveWord.Enabled=saveText.Enabled=!busy&&records.Any(r=>r.Checked&&!string.IsNullOrWhiteSpace(r.Text));
            if(!loading){loading=true;all.Checked=records.Count>0&&records.All(r=>r.Checked);loading=false;}
        }
        void ChooseFiles() {using(var d=new OpenFileDialog{Multiselect=true,Filter="음원 파일|*.mp3;*.wav;*.flac;*.ogg|모든 파일|*.*",Title="영어 음원 파일 선택"})if(d.ShowDialog(this)==DialogResult.OK)AddPaths(d.FileNames);}
        void ChooseFolder() {using(var d=new FolderBrowserDialog{Description="음원이 들어 있는 폴더를 선택하세요. 하위 폴더는 포함하지 않습니다."})if(d.ShowDialog(this)==DialogResult.OK)AddPaths(new[]{d.SelectedPath});}
        public void AddPaths(string[] paths) {
            if(busy)return;int count=0,skip=0;
            try {
                foreach(string source in paths) {
                    var candidates=Directory.Exists(source)?Directory.GetFiles(source):new[]{source};
                    foreach(string path in candidates) {
                        if(!Core.Supported(path)){skip++;continue;}
                        var file=new FileInfo(path);if(!file.Exists){skip++;continue;}
                        var old=records.FirstOrDefault(r=>string.Equals(r.Path,file.FullName,StringComparison.OrdinalIgnoreCase));
                        if(old!=null){if(old.Size!=file.Length||old.Modified!=file.LastWriteTimeUtc.Ticks){old.Size=file.Length;old.Modified=file.LastWriteTimeUtc.Ticks;old.Status="queued";old.Error="";}continue;}
                        records.Add(new Recording{Path=file.FullName,Size=file.Length,Modified=file.LastWriteTimeUtc.Ticks});count++;
                    }
                }
                RefreshList();SafeSave();status.Text=count+"개 파일을 추가했습니다."+(skip>0?" 지원하지 않는 파일 "+skip+"개는 제외했습니다.":"");
                if(current==null&&files.Items.Count>0)files.Items[0].Selected=true;
            }catch(Exception ex){MessageBox.Show(this,ex.Message,"파일 추가 오류");}
        }
        void SelectRecording() {if(loading)return;if(files.SelectedItems.Count==0)return;SafeSave();current=(Recording)files.SelectedItems[0].Tag;ShowCurrent();}
        void ShowCurrent() {
            loading=true;editor.Text=current==null?"":current.Text;editor.ReadOnly=current==null||current.Status=="running";loading=false;
            detail.Text=current==null?"목록에서 파일을 선택하세요":current.Name+"  ·  "+State(current)+(string.IsNullOrEmpty(current.Model)?"":"  ·  "+current.Model);
            if(current!=null&&current.Status=="error")status.Text=current.Error.Split('\n')[0];
        }
        void ScheduleSave(){dirty=true;saveTimer.Stop();saveTimer.Start();}
        void SafeSave(){
            try {if(current!=null&&dirty)Core.SaveTranscript(current);Core.SaveSession(records);dirty=false;}
            catch(Exception ex){status.Text="자동 저장 실패: "+ex.Message;}
        }
        void RemoveSelected(){if(busy)return;var selected=files.SelectedItems.Cast<ListViewItem>().Select(i=>(Recording)i.Tag).ToList();if(selected.Count==0)return;SafeSave();foreach(var r in selected)records.Remove(r);if(selected.Contains(current))current=null;RefreshList();ShowCurrent();SafeSave();status.Text="목록에서 뺐습니다. 원본 음원과 자동 저장 텍스트는 보존했습니다.";}
        void Play(){if(current==null)return;try{Process.Start(new ProcessStartInfo(current.Path){UseShellExecute=true});}catch(Exception ex){MessageBox.Show(this,ex.Message,"재생 오류");}}
        void Requeue(){if(busy||current==null)return;if(current.Text.Length>0&&MessageBox.Show(this,"다시 변환이 완료되면 이 파일의 편집한 스크립트를 새 결과로 바꿉니다. 계속할까요?","다시 변환",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;current.Status="queued";current.Checked=true;RefreshList();ShowCurrent();SafeSave();status.Text="다시 변환할 파일로 지정했습니다. 선택 파일 변환을 눌러 주세요.";}
        void SetBusy(bool value,string kind=""){
            busy=value;busyKind=kind;add.Enabled=folder.Enabled=remove.Enabled=run.Enabled=clear.Enabled=models.Enabled=all.Enabled=!value;stop.Enabled=value;UpdateModel();UpdateSummary();progress.Value=0;
        }
        async Task PrepareModel(){
            var model=(ModelSpec)models.SelectedItem;if(File.Exists(Core.ModelPath(model)))return;
            cancellation=new CancellationTokenSource();SetBusy(true,"download");status.Text="선택한 모델을 내려받습니다. 약 "+model.MB+"MB · 인터넷 연결 필요";
            try {await Core.DownloadModel(model,new Progress<int>(p=>{progress.Value=Math.Max(0,Math.Min(100,p));status.Text=p>=99?"다운로드 마무리 및 파일 검증 중…":"모델 다운로드 중  "+p+"%";}),cancellation.Token);status.Text="모델 준비 완료. 이제 인터넷 없이 변환할 수 있습니다.";}
            catch(OperationCanceledException){status.Text="다운로드를 중단했습니다. 미완성 파일은 삭제했습니다.";}
            catch(Exception ex){MessageBox.Show(this,ex.Message,"모델 다운로드 오류");status.Text="모델을 준비하지 못했습니다. 인터넷 연결을 확인해 주세요.";}
            finally{cancellation.Dispose();cancellation=null;SetBusy(false);}
        }
        async Task RunQueue(){
            SafeSave();var model=(ModelSpec)models.SelectedItem;
            try{Core.CheckRuntime();}catch(Exception ex){MessageBox.Show(this,ex.Message,"실행 구성 요소 필요");return;}
            if(!File.Exists(Core.ModelPath(model))){MessageBox.Show(this,"선택한 모델을 먼저 내려받아 주세요.","모델 준비 필요");return;}
            var queue=records.Where(r=>r.Checked&&r.Status!="done").OrderBy(r=>Core.SortKey(r.Name)).ToList();
            if(queue.Count==0){status.Text="변환할 파일을 선택해 주세요. 완료된 파일은 자동으로 건너뜁니다.";return;}
            cancellation=new CancellationTokenSource();SetBusy(true,"transcribe");int completed=0,failed=0;
            try{
                foreach(var r in queue){
                    cancellation.Token.ThrowIfCancellationRequested();r.Status="running";r.Error="";RefreshList();if(current==r)ShowCurrent();SafeSave();
                    status.Text=(completed+failed+1)+" / "+queue.Count+"  ·  "+r.Name+" 변환 중";
                    var displayProgress=new Progress<string>(p=>{status.Text=(completed+failed+1)+" / "+queue.Count+"  ·  "+r.Name+"  "+p;});
                    try{
                        string text=await Core.Transcribe(r.Path,model,cancellation.Token,p=>((IProgress<string>)displayProgress).Report(p));
                        r.Text=text;r.Status="done";r.Model=model.Id;Core.SaveTranscript(r);completed++;
                    }catch(OperationCanceledException){r.Status="queued";throw;}
                    catch(Exception ex){r.Status="error";r.Error=ex.Message;failed++;}
                    RefreshList();if(current==r)ShowCurrent();SafeSave();progress.Value=(completed+failed)*100/queue.Count;
                }
                status.Text="변환 완료  ·  "+completed+"개 완료"+(failed>0?" · "+failed+"개 오류 (파일을 선택해 확인)":" · 선택한 결과를 Word나 텍스트로 저장하세요.");
            }catch(OperationCanceledException){status.Text="중단했습니다. 완료된 파일 "+completed+"개는 저장했습니다. 다시 변환을 누르면 남은 파일부터 시작합니다.";}
            finally{cancellation.Dispose();cancellation=null;SetBusy(false);RefreshList();ShowCurrent();SafeSave();}
        }
        void Export(bool word){
            SafeSave();var selected=records.Where(r=>r.Checked&&!string.IsNullOrWhiteSpace(r.Text)).OrderBy(r=>Core.SortKey(r.Name)).ToList();if(selected.Count==0)return;
            using(var d=new SaveFileDialog{Filter=word?"Word 문서 (*.docx)|*.docx":"텍스트 파일 (*.txt)|*.txt",FileName=selected.Count==1?System.IO.Path.GetFileNameWithoutExtension(selected[0].Path)+"_스크립트":"영어_음원_스크립트",AddExtension=true,OverwritePrompt=true}){
                if(d.ShowDialog(this)!=DialogResult.OK)return;
                try{if(word)Core.ExportWord(d.FileName,selected);else Core.ExportText(d.FileName,selected);status.Text=selected.Count+"개 스크립트를 저장했습니다: "+d.FileName;}
                catch(Exception ex){MessageBox.Show(this,ex.Message,"저장 오류");}
            }
        }
        void OnClosing(object sender,FormClosingEventArgs e){
            if(busy){e.Cancel=true;if(MessageBox.Show(this,"진행 중인 작업을 중단할까요? 완료된 결과는 보존합니다.","작업 중",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes&&cancellation!=null)cancellation.Cancel();return;}
            SafeSave();saveTimer.Stop();
        }
        public void RenderPreview(string path){
            CreateControl();Show();Update();
            using(var bmp=new Bitmap(Width,Height)){DrawToBitmap(bmp,new Rectangle(0,0,Width,Height));bmp.Save(path);}
            Hide();
        }
        public async Task IntegrationTest(string sourceFolder) {
            string verification=System.IO.Path.Combine(Core.Data,"verification");Directory.CreateDirectory(verification);
            AddPaths(Enumerable.Range(1,5).Select(n=>System.IO.Path.Combine(sourceFolder,"Day"+n.ToString("00")+".mp3")).ToArray());
            AddPaths(new[]{System.IO.Path.Combine(sourceFolder,"Day01.mp3")});
            if(records.Count!=5)throw new Exception("Duplicate import failed");
            if(!models.Text.Contains("base.en"))throw new Exception("Model selection display failed");
            // First run processes only two checked recordings.
            foreach(ListViewItem i in files.Items)i.Checked=Array.IndexOf(new[]{"Day01.mp3","Day02.mp3"},((Recording)i.Tag).Name)>=0;
            await RunQueue();
            if(records.Count(r=>r.Status=="done")!=2)throw new Exception("Checked-file queue failed: "+string.Join(" | ",records.Select(r=>r.Name+": "+r.Error)));
            // Editing must survive selection changes and session reload.
            files.Items[0].Selected=true;current=records.First(r=>r.Name=="Day01.mp3");ShowCurrent();
            string original=editor.Text;editor.Text=original+"\r\nEDIT CHECK 한글 & <test>";SafeSave();
            var restored=Core.LoadSession();
            if(!restored.First(r=>r.Id==current.Id).Text.Contains("EDIT CHECK"))throw new Exception("Edited transcript persistence failed");
            editor.Text=original;SafeSave();
            // Resume skips completed recordings and processes the remaining three.
            foreach(ListViewItem i in files.Items)i.Checked=true;
            await RunQueue();
            if(records.Count(r=>r.Status=="done")!=5)throw new Exception("Resume failed: "+string.Join(" | ",records.Select(r=>r.Name+": "+r.Error)));
            Core.ExportWord(System.IO.Path.Combine(verification,"Day01_Day05.docx"),records.OrderBy(r=>Core.SortKey(r.Name)));
            Core.ExportText(System.IO.Path.Combine(verification,"Day01_Day05.txt"),records.OrderBy(r=>Core.SortKey(r.Name)));
            // A genuinely non-ASCII filename is copied to an ASCII native argument.
            string unicode=System.IO.Path.Combine(verification,"한글 파일 이름.mp3");
            File.Copy(System.IO.Path.Combine(sourceFolder,"Day01.mp3"),unicode,true);
            string unicodeText=await Core.Transcribe(unicode,Core.Models[0],CancellationToken.None,null);File.Delete(unicode);
            if(unicodeText.Length<30)throw new Exception("Unicode filename failed");
            // Cancel a live process and check that its temporary audio was removed.
            bool canceled=false;
            using(var c=new CancellationTokenSource()){
                c.CancelAfter(100);
                try{await Core.Transcribe(System.IO.Path.Combine(sourceFolder,"Day01.mp3"),Core.Models[0],c.Token,null);}catch(OperationCanceledException){canceled=true;}
            }
            if(!canceled||Directory.GetFiles(System.IO.Path.Combine(Core.Data,"temp")).Length!=0)throw new Exception("Cancellation cleanup failed");
            if(!records.All(r=>File.Exists(System.IO.Path.Combine(Core.Results,r.Id+".txt"))))throw new Exception("Automatic transcript storage failed");
            files.Items[0].Selected=true;current=records.First(r=>r.Name=="Day01.mp3");ShowCurrent();Update();
            using(var bmp=new Bitmap(Width,Height)){DrawToBitmap(bmp,new Rectangle(0,0,Width,Height));bmp.Save(System.IO.Path.Combine(verification,"app-preview.png"));}
            File.WriteAllText(System.IO.Path.Combine(verification,"integration-passed.txt"),"PASS: 5 real MP3 transcriptions, checked files, duplicate import, edit persistence, session reload, resume, Word and text export, Unicode filenames, live cancellation, temporary file cleanup.");
        }
    }
    static class Program {
        [STAThread]
        static int Main(string[] args){
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            try{
                if(args.Length>0&&args[0]=="--self-test")return SelfTest();
                if(args.Length==2&&args[0]=="--ui-test") {
                    int code=0;
                    using(var form=new StudioForm(false)){
                        form.Shown+=async(s,e)=>{try{await form.IntegrationTest(args[1]);}catch(Exception ex){File.WriteAllText(System.IO.Path.Combine(Core.Root,"test-error.txt"),ex.ToString());code=1;}finally{form.Close();}};
                        Application.Run(form);
                    }
                    return code;
                }
                if(args.Length==3&&args[0]=="--transcribe"){
                    string result=Core.Transcribe(System.IO.Path.GetFullPath(args[1]),Core.Models[0],CancellationToken.None,null).GetAwaiter().GetResult();
                    var r=new Recording{Path=args[1],Text=result,Status="done",Model="base.en"};
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(args[2])));
                    Core.ExportText(args[2]+".txt",new[]{r});Core.ExportWord(args[2]+".docx",new[]{r});return 0;
                }
                if(args.Length==2&&args[0]=="--preview"){using(var form=new StudioForm(false))form.RenderPreview(args[1]);return 0;}
                using(var form=new StudioForm(true))Application.Run(form);return 0;
            }catch(Exception ex){
                if(args.Length>0){File.WriteAllText(System.IO.Path.Combine(Core.Root,"test-error.txt"),ex.ToString());return 1;}
                MessageBox.Show(ex.Message,"Script Studio",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;
            }
        }
        static int SelfTest(){
            string dir=System.IO.Path.Combine(Core.Data,"self-test");Directory.CreateDirectory(dir);
            var records=new[]{new Recording{Path="Day01.mp3",Text="Hello & welcome. <A>\nSecond line. 한글",Status="done"},new Recording{Path="Day02.mp3",Text="It's a new day.",Status="done"}};
            Core.ExportWord(System.IO.Path.Combine(dir,"test.docx"),records);Core.ExportText(System.IO.Path.Combine(dir,"test.txt"),records);
            using(var z=ZipFile.OpenRead(System.IO.Path.Combine(dir,"test.docx"))){
                if(z.Entries.Count!=5)throw new Exception("DOCX package invalid");
                foreach(var entry in z.Entries)using(var s=entry.Open()){var xml=new XmlDocument();xml.Load(s);}
                using(var s=z.GetEntry("word/document.xml").Open()){var xml=new XmlDocument();xml.Load(s);if(!xml.InnerText.Contains("Hello & welcome. <A>")||!xml.InnerText.Contains("한글"))throw new Exception("DOCX text mismatch");}
            }
            if(string.CompareOrdinal(Core.SortKey("Day2.mp3"),Core.SortKey("Day10.mp3"))>=0)throw new Exception("Natural sorting failed");
            if(!Core.Supported("A.MP3")||Core.Supported("a.exe"))throw new Exception("File validation failed");
            if(Core.Quote("C:\\테스트 폴더\\음원.mp3")!="\"C:\\테스트 폴더\\음원.mp3\"")throw new Exception("Argument quoting failed");
            Core.AtomicWrite(System.IO.Path.Combine(dir,"atomic.txt"),"one");Core.AtomicWrite(System.IO.Path.Combine(dir,"atomic.txt"),"two");if(Core.ReadUtf8(System.IO.Path.Combine(dir,"atomic.txt"))!="two")throw new Exception("Atomic write failed");
            File.WriteAllText(System.IO.Path.Combine(dir,"passed.txt"),"PASS: Word XML, Unicode, text export, natural sort, file validation, argument quoting, atomic write.");return 0;
        }
    }
}
