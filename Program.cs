using System;
using System.Windows.Forms;

namespace ArtaleAI
{
    static class Program
    {
        /// <summary>
        /// ���ε{�����D�n�i�J�I�C
        /// </summary>
        [STAThread]
        static void Main()
        {

            // �ҥε�ı�˦�
            Application.EnableVisualStyles();

            // �]�w��r��Ķ�ۮe��
            Application.SetCompatibleTextRenderingDefault(false);

            // ����D���
            Application.Run(new MainForm());
        }
    }
}
