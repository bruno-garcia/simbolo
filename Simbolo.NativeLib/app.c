#include "dlfcn.h"
#define symLoad dlsym
#include <unistd.h>
#include <stdlib.h>
#include <stdio.h>

struct FrameInfo {
    char* mvid;
    char* method;
    int ilOffset;
} frameInfo;

struct Location {
    char* file;
    int line;
    int column;
} location;

int main(int argc, char *argv[])
{
    if (argc != 6) {
        printf("5 Arguments expected.\n");
        return 1;
    }
    char *symbolicationLibPath = argv[1];
    // TODO: Should be discovered based on mvid
    char *assemblyPath = argv[2];
    char *mvid = argv[3];
    char *method = argv[4];
    int ilOffset = atoi(argv[5]);

    if (access(symbolicationLibPath, F_OK) == -1)
    {
        printf("Symbolication library not found at %s\n", symbolicationLibPath);
        return 2;
    }

    if (access(assemblyPath, F_OK) == -1)
    {
        printf("Assembly not found at %s\n", assemblyPath);
        return 3;
    }

    struct FrameInfo fi;
    fi.mvid = mvid;
    fi.method = method;
    fi.ilOffset = ilOffset;

    typedef struct Location *(*symbolicate)();
    void *handle = dlopen(symbolicationLibPath, RTLD_LAZY);
    symbolicate func = symLoad(handle, "symbolicate");

    struct Location *loc = func(assemblyPath, fi);
    printf("%s %d:%d\n", loc->file, loc->line, loc->column);
}
