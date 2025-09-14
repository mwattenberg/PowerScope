import os

def replace_in_file(filepath, old, new):
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()
    except UnicodeDecodeError:
        print(f"Skipped (decode error): {filepath}")
        return
    if old in content:
        content = content.replace(old, new)
        with open(filepath, 'w', encoding='utf-8') as f:
            f.write(content)
        print(f"Replaced in: {filepath}")

def process_directory(root_dir, old, new):
    for dirpath, _, filenames in os.walk(root_dir):
        for filename in filenames:
            if filename.endswith('.xaml') or filename.endswith('.cs'):
                filepath = os.path.join(dirpath, filename)
                replace_in_file(filepath, old, new)

if __name__ == "__main__":
    # Set the root directory as needed
    root_dir = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
    process_directory(root_dir, "SerialPlotDN_WPF", "PowerScope")