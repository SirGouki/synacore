using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SFML.Graphics;
using SFML.Window;

namespace synacore.NET
{
    public class Emu
    {
        //TODO: RAM and Stack viewer

        //Registers
        UInt16[] r;
        UInt16 PC = 0; //Program Counter
        UInt16 SP = 0; //Stack Pointer

        //Memory
        UInt16[] RAM;
        List<UInt16> stack;

        //flags
        bool debug = true;
        bool paused = false;
        bool terminate = false;

        //opcodes
        Dictionary<UInt16, Delegate> instructions;
        UInt16 opcode;

        //other
        UInt16 mod = 32768; //all ints are % this number

        public Emu()
        {
            //init starts here

            //this only needs to be done once, so its handled outside reset (opcodes don't change on reset)
            instructions = new Dictionary<UInt16, Delegate>();

            instructions[0] = new Action(halt);
            instructions[1] = new Action<UInt16, UInt16>(setR);
            instructions[2] = new Action<UInt16>(push);
            instructions[3] = new Action<UInt16>(pop);
            instructions[4] = new Action<UInt16, UInt16, UInt16>(checkEQ);
            instructions[5] = new Action<UInt16, UInt16, UInt16>(checkGT);
            instructions[6] = new Action<UInt16>(JMP);
            instructions[7] = new Action<UInt16, UInt16>(JNZ);
            instructions[8] = new Action<UInt16, UInt16>(JEZ);
            instructions[9] = new Action<UInt16, UInt16, UInt16>(setRADD);
            instructions[10] = new Action<UInt16, UInt16, UInt16>(setRMUL);
            instructions[11] = new Action<UInt16, UInt16, UInt16>(setRMOD);
            instructions[12] = new Action<UInt16, UInt16, UInt16>(setRAND);
            instructions[13] = new Action<UInt16, UInt16, UInt16>(setROR);
            instructions[14] = new Action<UInt16, UInt16>(setRNOT);
            instructions[15] = new Action<UInt16, UInt16>(rMem);
            instructions[16] = new Action<UInt16, UInt16>(wMem);
            instructions[17] = new Action<UInt16>(call);
            instructions[18] = new Action(ret);
            instructions[19] = new Action<UInt16>(WriteChar);
            instructions[20] = new Action<UInt16>(getInput);
            instructions[21] = new Action(NOP);

            //init everything else
            Reset();

        }

        private void Reset()
        {
            r = new UInt16[]
            {
                0, 0, 0, 0,
                0, 0, 0, 0
            };

            PC = 0;
            SP = 0;

            RAM = new ushort[32767];
            stack = new List<UInt16>();
        }

        public void LoadROM()
        {
            //since the rom will ALWAYS be challenge.bin, we're not getting a filename
            string filename = "challenge.bin";
            byte[] rom = new byte[32];

            try
            {
                rom = File.ReadAllBytes(filename);
            }
            catch (Exception e)
            {
                ErrorMsg(e.Message);
                Environment.Exit(0);
            }

            Array.Copy(rom, 0, RAM, 0, rom.Length);
        }


        public void Emulate()
        {
            while (!terminate)
            {
                if (!paused)
                {
                    //fetch next opcode
                    opcode = Fetch();

                    //execute the instruction
                    if (instructions.ContainsKey(opcode))
                    {
                        Execute(opcode);
                    }
                    else
                    {
                        ErrorMsg($"Invalid instruction: {opcode}");
                    }
                }
            }
        }

        private UInt16 Fetch()
        {
            UInt16 final = RAM[PC];

            PC++;

            return final;
        }

        private void Execute(UInt16 op)
        {

            //this *should* prevent having to load a check for each individual opcode
            if(instructions[op].Method.GetParameters().Length > 2)
            {
                UInt16 a = GetNextArg();
                UInt16 b = GetNextArg();
                UInt16 c = GetNextArg();

                if(a > 32775 || b > 32775 || c > 32775)
                {
                    ErrorOOB(a, b, c);
                    return;
                }

                instructions[op].DynamicInvoke(a, b, c);
            }
            else if (instructions[op].Method.GetParameters().Length > 1)
            {
                UInt16 a = GetNextArg();
                UInt16 b = GetNextArg();
                

                if (a > 32775 || b > 32775)
                {
                    ErrorOOB(a, b);
                    return;
                }

                instructions[op].DynamicInvoke(a, b);
            }
            else if (instructions[op].Method.GetParameters().Length > 0)
            {
                UInt16 a = GetNextArg();
                
                if (a > 32775)
                {
                    ErrorOOB(a);
                    return;
                }

                instructions[op].DynamicInvoke(a);
            }
            else
            {
                instructions[op].DynamicInvoke();
            }

        }

        private UInt16 GetNextArg()
        {
            //args are little endian, so we need to convert
            byte lo = (byte)(RAM[PC] >> 8);
            byte hi = (byte)(RAM[PC] & 0xFF);


            UInt16 arg = (UInt16)((hi << 8) | lo);
            PC += 2;
            return arg;
        }

        private void halt()
        {
            PC = 0;
            terminate = true;
        }

        private void setR(UInt16 reg, UInt16 v)
        {
            //set register[r] to v
            r[reg % 8] = v;
        }

        private void push(UInt16 a)
        {
            if (a > 32767)
            {
                stack.Add(r[a % 8]);
            }
            else
            {
                stack.Add(a);
            }
        }

        private void pop(UInt16 a)
        {
            if (stack.Count > 0)
            {
                UInt16 v = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);

                if (a > 32767)
                {
                    //store it in a register
                    r[a % 8] = v;
                }
                else
                {
                    //store it in ram
                    SetRAM(a, v);
                }
            }
            else
            {
                ErrorMsg("Cannot pop from an empty stack!");
            }
        }

        private void checkEQ(UInt16 a, UInt16 b, UInt16 c)
        {
            //if b == c, a = 1, else a = 0

            if (b > 32767)  b = r[b % 8];
            
            if (c > 32767)  c = r[c % 8];
            
            if (a > 32767)
            {
                //store it in a register
                r[a % 8] = (UInt16)((b == c) ? 1 : 0);
            }
            else
            {
                //store it in ram
                SetRAM(a, (UInt16)((b == c) ? 1 : 0));
            }
        }

        private void checkGT(UInt16 a, UInt16 b, UInt16 c)
        {
            //if b > c, a = 1, else a = 0

            if (b > 32767)  b = r[b % 8];

            if (c > 32767)  c = r[c % 8];
            
            if (a > 32767)
            {
                //store it in a register
                r[a % 8] = (UInt16)((b > c) ? 1 : 0);
            }
            else
            {
                //store it in ram
                SetRAM(a, (UInt16)((b > c) ? 1 : 0));
            }
        }

        private void JMP(UInt16 a)
        {
            if (a > 32767)
                PC = r[a % 8];
            else
                PC = a;
        }

        private void JNZ(UInt16 a, UInt16 b)
        {
            if(b > 32767)  b = r[b % 8];

            //jump to b if a != 0
            if (a > 32767) a = r[a % 8];

            if(a != 0)
            {
                PC = b;
            }
        }

        private void JEZ(UInt16 a, UInt16 b)
        {
            if (b > 32767) b = r[b % 8];

            if (a > 32767) a = r[a % 8];

            if(a == 0)
            {
                PC = b;
            }
        }

        private void setRADD(UInt16 a, UInt16 b, UInt16 c)
        {
            if (b > 32767) b = r[b % 8];
            if (c > 32767) c = r[c % 8];

            if(a > 32767)
            {
                r[a % 8] = (UInt16)((b + c) % mod);
            }
            else
            {
                SetRAM(a, (UInt16)((b + c) % mod));
            }
        }

        private void setRMUL(UInt16 a, UInt16 b, UInt16 c)
        {
            if (b > 32767) b = r[b % 8];
            if (c > 32767) c = r[c % 8];

            if (a > 32767)
            {
                r[a % 8] = (UInt16)((b * c) % mod);
            }
            else
            {
                SetRAM(a, (UInt16)((b * c) % mod));
            }
        }

        private void setRMOD(UInt16 a, UInt16 b, UInt16 c)
        {
            if (b > 32767) b = r[b % 8];
            if (c > 32767) c = r[c % 8];

            if (a > 32767)
            {
                r[a % 8] = (UInt16)(b % c);
            }
            else
            {
                SetRAM(a, (UInt16)(b % c));
            }
        }

        private void setRAND(UInt16 a, UInt16 b, UInt16 c)
        {
            if (b > 32767) b = r[b % 8];
            if (c > 32767) c = r[c % 8];

            if (a > 32767)
            {
                r[a % 8] = (UInt16)(b & c);
            }
            else
            {
                SetRAM(a, (UInt16)(b & c));
            }
        }

        private void setROR(UInt16 a, UInt16 b, UInt16 c)
        {
            if (b > 32767) b = r[b % 8];
            if (c > 32767) c = r[c % 8];

            if (a > 32767)
            {
                r[a % 8] = (UInt16)(b | c);
            }
            else
            {
                SetRAM(a, (UInt16)(b | c));
            }
        }

        private void setRNOT(UInt16 a, UInt16 b)
        {
            if (b > 32767) b = r[b % 8];

            b = (UInt16)(b << 1); //shift first to ignore the first bit

            UInt16 final = (UInt16)((~b) >> 1); //flip then shift back, MSB SHOULD be 0 now

            if(a > 32767)
            {
                r[a % 8] = final;
            }
            else
            {
                SetRAM(a, final);
            }


        }

        private void rMem(UInt16 a, UInt16 b)
        {
            //read from $b, store the result at reg[a] or ram[a]
            if (b > 32767) b = r[b % 8];

            byte hi, lo;
            hi = (byte)(RAM[b % 8] & 0xFF);
            lo = (byte)(RAM[b % 8] >> 8);

            UInt16 val = (UInt16)((hi) << 8 | lo);

            if(a > 32767)
            {
                r[a % 8] = val;
            }
            else
            {
                SetRAM(a, val);
            }
        }

        private void wMem(UInt16 a, UInt16 b)
        {
            //write the value of b to memory reg[a] or a
            if (b > 32767) b = r[b % 8];

            if(a > 32767)
            {
                SetRAM((UInt16)(a % 8), b);
            }
            else
            {
                SetRAM(a, b);
            }
        }

        private void call(UInt16 a)
        {
            //write the next instructon address to the stack, then jump a
            if (a > 32767) a = r[a % 8];

            stack.Add(PC);

            PC = a;

        }

        private void ret()
        {
            UInt16 dest = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
        }

        private void WriteChar(UInt16 a)
        {
            if (a > 32767) a = r[a % 8];

            char c;
            c = Convert.ToChar(a);

            Console.Write(c); //this might get ugly, consider building a string of chars
        }

        private void getInput(UInt16 a)
        {
            string chars = Console.ReadLine();
            int i = 0;
            foreach(char c in chars)
            {
                if (a > 32767)
                    RAM[r[a % 8] + i] = Convert.ToUInt16(c);
                else
                    RAM[a + i] = Convert.ToUInt16(c);

                i++;
            }
        }

        private void NOP()
        {
            //literally, do nothing
            return;
        }

        private void SetRAM(UInt16 index, UInt16 val)
        {
            byte hi = (byte)(val >> 8);
            byte lo = (byte)(val & 0xFF);

            //convert to little endian
            UInt16 final = (UInt16)(lo << 8 | hi);

            RAM[index] = final;
        }

        private void ErrorOOB(UInt16 a)
        {
            ErrorMsg($"Argument out of bounds: args a:{a}. Expected 0-32775.");
        }
        private void ErrorOOB(UInt16 a, UInt16 b)
        {
            ErrorMsg($"Argument out of bounds: args a:{a}, b:{b}. Expected 0-32775.");
        }
        private void ErrorOOB(UInt16 a, UInt16 b, UInt16 c)
        {
            ErrorMsg($"Argument out of bounds: args a:{a}, b:{b}, c:{c}. Expected 0-32775.");
        }

        private void SystemMsg(string msg)
        {
            string final = "[";
            Console.ForegroundColor = ConsoleColor.Cyan;
            final += "System";
            Console.ResetColor();
            final += $"]: {msg}";
            Console.WriteLine(final);
        }

        private void ErrorMsg(string msg)
        {
            string final = "[";
            Console.ForegroundColor = ConsoleColor.Red;
            final += "ERROR";
            Console.ResetColor();
            final += $"]: {msg}";
            Console.WriteLine(final);
        }


    }
}
