/*using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AgentSwarmSimulation
{
    // Il Form principale rinominato in Form3
    public partial class Form3Copy : Form
    {
        private SwarmCanvas swarmCanvas;

        public Form3Copy()
        {
            InitializeComponent();
            InitializeFormSettings();
            InitializeWorkspace();
        }

        // Questo metodo sostituisce la parte solitamente generata dal designer
        private void InitializeComponent()
        {
            this.SuspendLayout();
            // 
            // Form3
            // 
            this.ClientSize = new System.Drawing.Size(1000, 700);
            this.Name = "Form3";
            this.ResumeLayout(false);
        }

        private void InitializeFormSettings()
        {
            this.Text = "AI Agent Swarm Workspace - Form3";
            this.BackColor = Color.FromArgb(10, 14, 23); // Tema scuro Cyberpunk
            this.StartPosition = FormStartPosition.CenterScreen;
        }

        private void InitializeWorkspace()
        {
            // Inizializza il canvas personalizzato per l'animazione dei nodi/agenti
            swarmCanvas = new SwarmCanvas();
            swarmCanvas.Dock = DockStyle.Fill;
            this.Controls.Add(swarmCanvas);

            // Pannello inferiore che simula la barra di comando degli agenti
            Panel bottomPanel = new Panel();
            bottomPanel.Height = 60;
            bottomPanel.Dock = DockStyle.Bottom;
            bottomPanel.BackColor = Color.FromArgb(20, 25, 40);

            Label statusLabel = new Label();
            statusLabel.Text = "FORM3 STATUS: 24 Active Agents Orchestrating Task [Refining Codebase]...";
            statusLabel.ForeColor = Color.FromArgb(0, 255, 200);
            statusLabel.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            statusLabel.AutoSize = true;
            statusLabel.Location = new Point(20, 20);

            bottomPanel.Controls.Add(statusLabel);
            this.Controls.Add(bottomPanel);
        }
    }

    // Canvas personalizzato con Double Buffering attivo per evitare sfarfallii
    public class SwarmCanvas : UserControl
    {
        private System.Timers.Timer animationTimer;
        private List<SwarmAgent> agents;
        private PointF masterNode;
        private Random rand = new Random();
        private int agentCount = 24;

        public SwarmCanvas()
        {
            this.DoubleBuffered = true;
            this.BackColor = Color.FromArgb(13, 17, 28);

            InitializeAgents();

            // Setup del loop di animazione (~60 FPS)
            animationTimer = new System.Timers.Timer();
            animationTimer.Interval = 16;
            animationTimer.Elapsed += AnimationTimer_Tick;
            animationTimer.Start();
        }

        private void InitializeAgents()
        {
            agents = new List<SwarmAgent>();
            for (int i = 0; i < agentCount; i++)
            {
                agents.Add(new SwarmAgent(rand, 1000, 700));
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // Centra il nodo orchestratore principale in base alle dimensioni della finestra
            masterNode = new PointF(this.Width / 2f, this.Height / 2f);
        }

        private void AnimationTimer_Tick(object sender, EventArgs e)
        {
            // Aggiorna la fisica di ogni agente verso il centro
            foreach (var agent in agents)
            {
                agent.Update(masterNode, this.Width, this.Height);
            }

            // Forza il ridisegno del controllo
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 1. Disegna le connessioni di rete (Linee dinamiche tra agenti vicini)
            using (Pen connectionPen = new Pen(Color.FromArgb(40, 0, 180, 216), 1))
            {
                for (int i = 0; i < agents.Count; i++)
                {
                    for (int j = i + 1; j < agents.Count; j++)
                    {
                        float dist = GetDistance(agents[i].Position, agents[j].Position);
                        if (dist < 120)
                        {
                            // Dissolvenza della linea in base alla distanza
                            int alpha = (int)(200 * (1.0f - (dist / 120)));
                            connectionPen.Color = Color.FromArgb(alpha, 0, 180, 216);
                            g.DrawLine(connectionPen, agents[i].Position, agents[j].Position);
                        }
                    }

                    // Disegna la connessione verso il Master Node Centrale
                    float distToMaster = GetDistance(agents[i].Position, masterNode);
                    if (distToMaster < 250)
                    {
                        int alpha = (int)(150 * (1.0f - (distToMaster / 250)));
                        using (Pen masterLinePen = new Pen(Color.FromArgb(alpha, 114, 9, 183), 1))
                        {
                            g.DrawLine(masterLinePen, agents[i].Position, masterNode);
                        }
                    }
                }
            }

            // 2. Disegna il Master Node Centrale (L'obiettivo del cluster)
            using (SolidBrush masterBrush = new SolidBrush(Color.FromArgb(114, 9, 183)))
            {
                g.FillEllipse(masterBrush, masterNode.X - 15, masterNode.Y - 15, 30, 30);
                using (SolidBrush coreBrush = new SolidBrush(Color.FromArgb(0, 245, 212)))
                {
                    g.FillEllipse(coreBrush, masterNode.X - 6, masterNode.Y - 6, 12, 12);
                }
            }

            // 3. Disegna i singoli Agenti dello Sciame
            foreach (var agent in agents)
            {
                // Pulsazione dell'alone luminoso esterno
                int glowAlpha = 50 + (int)(30 * Math.Sin(Environment.TickCount * 0.005 + agent.Speed));
                using (SolidBrush glowBrush = new SolidBrush(Color.FromArgb(glowAlpha, 0, 245, 212)))
                {
                    g.FillEllipse(glowBrush, agent.Position.X - agent.Size, agent.Position.Y - agent.Size, agent.Size * 2, agent.Size * 2);
                }

                // Nucleo solido dell'agente
                using (SolidBrush nodeBrush = new SolidBrush(Color.White))
                {
                    g.FillEllipse(nodeBrush, agent.Position.X - 3, agent.Position.Y - 3, 6, 6);
                }
            }
        }

        private float GetDistance(PointF p1, PointF p2)
        {
            return (float)Math.Sqrt(Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2));
        }
    }

    // Entità logica dell'Agente (rinominata in SwarmAgent per evitare conflitti)
    public class SwarmAgent
    {
        public PointF Position;
        private PointF velocity;
        public float Speed;
        public float Size;
        private Random rand;

        public SwarmAgent(Random r, int maxX, int maxY)
        {
            this.rand = r;
            this.Position = new PointF(rand.Next(0, maxX), rand.Next(0, maxY));
            this.velocity = new PointF((float)(rand.NextDouble() * 4 - 2), (float)(rand.NextDouble() * 4 - 2));
            this.Speed = (float)(rand.NextDouble() * 1.5 + 1.0);
            this.Size = rand.Next(10, 22);
        }

        public void Update(PointF target, int width, int height)
        {
            // Algoritmo di attrazione verso il target con l'aggiunta di rumore casuale (effetto sciame)
            float dx = target.X - Position.X;
            float dy = target.Y - Position.Y;

            float pullFactor = 0.003f;

            velocity.X += dx * pullFactor + (float)(rand.NextDouble() * 0.4 - 0.2);
            velocity.Y += dy * pullFactor + (float)(rand.NextDouble() * 0.4 - 0.2);

            // Limitazione della velocità massima dello sciame
            float currentSpeed = (float)Math.Sqrt(velocity.X * velocity.X + velocity.Y * velocity.Y);
            if (currentSpeed > Speed * 3)
            {
                velocity.X = (velocity.X / currentSpeed) * Speed * 3;
                velocity.Y = (velocity.Y / currentSpeed) * Speed * 3;
            }

            Position.X += velocity.X;
            Position.Y += velocity.Y;

            // Rimbalzo morbido ai bordi del pannello
            if (Position.X < 0 || Position.X > width) velocity.X *= -1;
            if (Position.Y < 0 || Position.Y > height) velocity.Y *= -1;
        }
    }
}*/