import os

OUTPUT_DIR = "large_scripts"
NUM_FILES = 100
FUNCS_PER_FILE = 100
LABELS_PER_FILE = 100

os.makedirs(OUTPUT_DIR, exist_ok=True)

for file_index in range(NUM_FILES):
    filename = f"large_{file_index:03}.gs"
    filepath = os.path.join(OUTPUT_DIR, filename)
    lines = [f"// {filename}"]

    # Generate functions
    for func_index in range(FUNCS_PER_FILE):
        func_name = f"func_{file_index}_{func_index}"
        prev_func = f"~func_{file_index}_{func_index - 1}($x)" if func_index > 0 else "$x"
        lines.append(f"""
func {func_name}(int $x) returns int
\tprintln("In {func_name}")
\tint $y = {prev_func}
\treturn $y + 1
""")

    # Generate labels with mod(x, y)
    for label_index in range(LABELS_PER_FILE):
        label_name = f"label_{file_index}_{label_index}"
        lines.append(f"""
label {label_name}()
\tprintln("Entered {label_name}")
\tif (mod({label_index}, 2) == 0)
\t\tprintln("Even index")
\telse
\t\tprintln("Odd index")
""")

    # Cross-call from previous file
    if file_index > 0:
        lines.append(f"""
func cross_call_{file_index}(int $z) returns int
\tprintln("Calling cross file func from file {file_index}")
\treturn ~func_{file_index - 1}_0($z)
""")

    with open(filepath, "w") as f:
        f.write("\n".join(lines))

# Create entry point script
entry_path = os.path.join(OUTPUT_DIR, "main_entry.gs")
with open(entry_path, "w") as f:
    f.write(f"""// main_entry.gs
npc_op_1 stress_entry()
\tprintln("Starting large script test")
\tint $result = ~func_{NUM_FILES - 1}_{FUNCS_PER_FILE - 1}(0)
\tprintln("Final result:")
\tprintln(int_2_str($result))
""")

print(f"✅ Generated {NUM_FILES + 1} large script files in '{OUTPUT_DIR}'")
