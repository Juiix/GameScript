import os

OUTPUT_DIR = "generated_scripts"
NUM_FILES = 1000

os.makedirs(OUTPUT_DIR, exist_ok=True)

for i in range(NUM_FILES):
    filename = f"script_{i:04}.gs"
    filepath = os.path.join(OUTPUT_DIR, filename)
    
    func_name = f"func_{i:04}"
    prev_func_call = f"~func_{i-1:04}($x)" if i > 0 else "$x"

    content = f"""// {filename}
func {func_name}(int $x) returns int
\tprintln("Calling {func_name}")
\tint $y = {prev_func_call}
\tprintln("Result from previous:")
\tprintln(int_2_str($y))
\treturn $y + 1
"""

    with open(filepath, "w") as f:
        f.write(content)

# Generate the entry file that calls the last one
entry_path = os.path.join(OUTPUT_DIR, "main_entry.gs")
with open(entry_path, "w") as f:
    f.write("""// main_entry.gs
npc_op_1 stress_entry()
\tprintln("Starting large workspace test")
\tint $final = ~func_0999(0)
\tprintln("Final result:")
\tprintln(int_2_str($final))
""")

print(f"Generated {NUM_FILES + 1} scripts in: {OUTPUT_DIR}")
