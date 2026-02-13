/**
 * CIL2CPP Runtime Tests - Type System
 */

#include <gtest/gtest.h>
#include <cil2cpp/cil2cpp.h>

using namespace cil2cpp;

// Create a type hierarchy for testing:
// Object -> Animal -> Dog
//                  -> Cat
// IRunnable (interface)

static TypeInfo ObjectType = {
    .name = "Object",
    .namespace_name = "System",
    .full_name = "System.Object",
    .base_type = nullptr,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = sizeof(Object),
    .element_size = 0,
    .flags = TypeFlags::None,
    .vtable = nullptr,
    .fields = nullptr,
    .field_count = 0,
    .methods = nullptr,
    .method_count = 0,
    .default_ctor = nullptr,
    .finalizer = nullptr,
    .interface_vtables = nullptr,
    .interface_vtable_count = 0,
};

static TypeInfo IRunnableType = {
    .name = "IRunnable",
    .namespace_name = "Tests",
    .full_name = "Tests.IRunnable",
    .base_type = nullptr,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = 0,
    .element_size = 0,
    .flags = TypeFlags::Interface,
    .vtable = nullptr,
    .fields = nullptr,
    .field_count = 0,
    .methods = nullptr,
    .method_count = 0,
    .default_ctor = nullptr,
    .finalizer = nullptr,
    .interface_vtables = nullptr,
    .interface_vtable_count = 0,
};

static TypeInfo* DogInterfaces[] = { &IRunnableType };

static TypeInfo AnimalType = {
    .name = "Animal",
    .namespace_name = "Tests",
    .full_name = "Tests.Animal",
    .base_type = &ObjectType,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = sizeof(Object) + 8,
    .element_size = 0,
    .flags = TypeFlags::None,
    .vtable = nullptr,
    .fields = nullptr,
    .field_count = 0,
    .methods = nullptr,
    .method_count = 0,
    .default_ctor = nullptr,
    .finalizer = nullptr,
    .interface_vtables = nullptr,
    .interface_vtable_count = 0,
};

static TypeInfo DogType = {
    .name = "Dog",
    .namespace_name = "Tests",
    .full_name = "Tests.Dog",
    .base_type = &AnimalType,
    .interfaces = DogInterfaces,
    .interface_count = 1,
    .instance_size = sizeof(Object) + 16,
    .element_size = 0,
    .flags = TypeFlags::None,
    .vtable = nullptr,
    .fields = nullptr,
    .field_count = 0,
    .methods = nullptr,
    .method_count = 0,
    .default_ctor = nullptr,
    .finalizer = nullptr,
    .interface_vtables = nullptr,
    .interface_vtable_count = 0,
};

static TypeInfo CatType = {
    .name = "Cat",
    .namespace_name = "Tests",
    .full_name = "Tests.Cat",
    .base_type = &AnimalType,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = sizeof(Object) + 16,
    .element_size = 0,
    .flags = TypeFlags::None,
    .vtable = nullptr,
    .fields = nullptr,
    .field_count = 0,
    .methods = nullptr,
    .method_count = 0,
    .default_ctor = nullptr,
    .finalizer = nullptr,
    .interface_vtables = nullptr,
    .interface_vtable_count = 0,
};

class TypeSystemTest : public ::testing::Test {
protected:
    void SetUp() override {
        runtime_init();
    }

    void TearDown() override {
        runtime_shutdown();
    }
};

// ===== type_is_subclass_of =====

TEST_F(TypeSystemTest, IsSubclassOf_Direct) {
    EXPECT_TRUE(type_is_subclass_of(&DogType, &AnimalType));
}

TEST_F(TypeSystemTest, IsSubclassOf_Transitive) {
    EXPECT_TRUE(type_is_subclass_of(&DogType, &ObjectType));
}

TEST_F(TypeSystemTest, IsSubclassOf_NotSubclass) {
    EXPECT_FALSE(type_is_subclass_of(&CatType, &DogType));
}

TEST_F(TypeSystemTest, IsSubclassOf_SameType_False) {
    EXPECT_FALSE(type_is_subclass_of(&DogType, &DogType));
}

TEST_F(TypeSystemTest, IsSubclassOf_Null) {
    EXPECT_FALSE(type_is_subclass_of(nullptr, &ObjectType));
    EXPECT_FALSE(type_is_subclass_of(&DogType, nullptr));
}

// ===== type_is_assignable_from =====

TEST_F(TypeSystemTest, IsAssignableFrom_SameType) {
    EXPECT_TRUE(type_is_assignable_from(&DogType, &DogType));
}

TEST_F(TypeSystemTest, IsAssignableFrom_BaseFromDerived) {
    EXPECT_TRUE(type_is_assignable_from(&AnimalType, &DogType));
}

TEST_F(TypeSystemTest, IsAssignableFrom_DerivedFromBase_False) {
    EXPECT_FALSE(type_is_assignable_from(&DogType, &AnimalType));
}

TEST_F(TypeSystemTest, IsAssignableFrom_InterfaceFromImplementor) {
    EXPECT_TRUE(type_is_assignable_from(&IRunnableType, &DogType));
}

TEST_F(TypeSystemTest, IsAssignableFrom_InterfaceFromNonImplementor) {
    EXPECT_FALSE(type_is_assignable_from(&IRunnableType, &CatType));
}

// ===== type_implements_interface =====

TEST_F(TypeSystemTest, ImplementsInterface_Direct) {
    EXPECT_TRUE(type_implements_interface(&DogType, &IRunnableType));
}

TEST_F(TypeSystemTest, ImplementsInterface_NotImplemented) {
    EXPECT_FALSE(type_implements_interface(&CatType, &IRunnableType));
}

TEST_F(TypeSystemTest, ImplementsInterface_Null) {
    EXPECT_FALSE(type_implements_interface(nullptr, &IRunnableType));
    EXPECT_FALSE(type_implements_interface(&DogType, nullptr));
}

// ===== Type registry =====

TEST_F(TypeSystemTest, Register_ThenGetByName) {
    type_register(&DogType);
    TypeInfo* found = type_get_by_name("Tests.Dog");
    EXPECT_EQ(found, &DogType);
}

TEST_F(TypeSystemTest, GetByName_NotRegistered_ReturnsNull) {
    TypeInfo* found = type_get_by_name("NonExistent.Type");
    EXPECT_EQ(found, nullptr);
}

TEST_F(TypeSystemTest, Register_NullType_NoOp) {
    type_register(nullptr);  // Should not crash
    SUCCEED();
}

// ===== TypeFlags =====

TEST_F(TypeSystemTest, TypeFlags_BitwiseOr) {
    auto flags = TypeFlags::ValueType | TypeFlags::Sealed;
    EXPECT_TRUE(flags & TypeFlags::ValueType);
    EXPECT_TRUE(flags & TypeFlags::Sealed);
    EXPECT_FALSE(flags & TypeFlags::Interface);
}

TEST_F(TypeSystemTest, TypeFlags_None) {
    EXPECT_FALSE(TypeFlags::None & TypeFlags::ValueType);
}

// ===== Interface VTable Dispatch =====

static int32_t test_vtable_method(void* self) {
    return 42;
}

static int32_t test_iface_method(void* self) {
    return 99;
}

TEST_F(TypeSystemTest, GetInterfaceVTable_Found_ReturnsNonNull) {
    // Set up Dog with an interface vtable for IRunnable
    static void* irunnable_methods[] = { (void*)test_iface_method };
    static InterfaceVTable dog_iface_vtables[] = {
        { &IRunnableType, irunnable_methods, 1 }
    };
    DogType.interface_vtables = dog_iface_vtables;
    DogType.interface_vtable_count = 1;

    auto* result = type_get_interface_vtable(&DogType, &IRunnableType);
    EXPECT_NE(result, nullptr);
    EXPECT_EQ(result->interface_type, &IRunnableType);
    EXPECT_EQ(result->method_count, 1u);

    DogType.interface_vtables = nullptr;
    DogType.interface_vtable_count = 0;
}

TEST_F(TypeSystemTest, GetInterfaceVTable_NotFound_ReturnsNull) {
    auto* result = type_get_interface_vtable(&CatType, &IRunnableType);
    EXPECT_EQ(result, nullptr);
}

TEST_F(TypeSystemTest, GetInterfaceVTable_InheritedFromBase) {
    // Set up Animal with an interface vtable, Dog should inherit it
    static TypeInfo IWalkableType = {
        .name = "IWalkable",
        .namespace_name = "Tests",
        .full_name = "Tests.IWalkable",
        .base_type = nullptr,
        .interfaces = nullptr,
        .interface_count = 0,
        .instance_size = 0,
        .element_size = 0,
        .flags = TypeFlags::Interface,
        .vtable = nullptr,
        .fields = nullptr,
        .field_count = 0,
        .methods = nullptr,
        .method_count = 0,
        .default_ctor = nullptr,
        .finalizer = nullptr,
        .interface_vtables = nullptr,
        .interface_vtable_count = 0,
    };
    static void* walkable_methods[] = { (void*)test_iface_method };
    static InterfaceVTable animal_iface_vtables[] = {
        { &IWalkableType, walkable_methods, 1 }
    };
    AnimalType.interface_vtables = animal_iface_vtables;
    AnimalType.interface_vtable_count = 1;

    // Dog inherits from Animal, should find Animal's interface vtable
    auto* result = type_get_interface_vtable(&DogType, &IWalkableType);
    EXPECT_NE(result, nullptr);
    EXPECT_EQ(result->interface_type, &IWalkableType);

    AnimalType.interface_vtables = nullptr;
    AnimalType.interface_vtable_count = 0;
}

TEST_F(TypeSystemTest, VTable_FunctionPointer_Dispatches) {
    static void* vtable_methods[] = { (void*)test_vtable_method };
    static VTable dog_vtable = { &DogType, vtable_methods, 1 };
    DogType.vtable = &dog_vtable;

    // Simulate virtual dispatch through vtable
    auto fn = (int32_t(*)(void*))DogType.vtable->methods[0];
    EXPECT_EQ(fn(nullptr), 42);

    DogType.vtable = nullptr;
}

TEST_F(TypeSystemTest, InterfaceVTable_FunctionPointer_Dispatches) {
    static void* iface_methods[] = { (void*)test_iface_method };
    static InterfaceVTable dog_iface_vtables[] = {
        { &IRunnableType, iface_methods, 1 }
    };
    DogType.interface_vtables = dog_iface_vtables;
    DogType.interface_vtable_count = 1;

    auto* ivtable = type_get_interface_vtable(&DogType, &IRunnableType);
    ASSERT_NE(ivtable, nullptr);
    auto fn = (int32_t(*)(void*))ivtable->methods[0];
    EXPECT_EQ(fn(nullptr), 99);

    DogType.interface_vtables = nullptr;
    DogType.interface_vtable_count = 0;
}
