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
            // ��l�� COM�A�T�O���T�����J�Ҧ�
            System.Threading.Thread.CurrentThread.SetApartmentState(System.Threading.ApartmentState.STA);

            // �ҥε�ı�˦�
            Application.EnableVisualStyles();

            // �]�w��r��Ķ�ۮe��
            Application.SetCompatibleTextRenderingDefault(false);

            // ����D���
            Application.Run(new Form1());
        }
    }
}
