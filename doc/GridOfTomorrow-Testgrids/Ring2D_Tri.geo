cl1 = 1;
Point(1) = {1, 0, 0, cl1};
Point(2) = {-1, 0, 0, cl1};
Point(3) = {-2, 0, 0, cl1};
Point(4) = {2, 0, 0, cl1};
Point(5) = {0, 1, 0, cl1};
Point(6) = {0, -1, 0, cl1};
Point(7) = {0, -2, 0, cl1};
Point(8) = {0, 2, 0, cl1};
Point(9) = {0, 0, 0, cl1};
Circle(1) = {5, 9, 1};
Circle(2) = {1, 9, 6};
Circle(3) = {6, 9, 2};
Circle(4) = {2, 9, 5};
Circle(5) = {8, 9, 4};
Circle(6) = {4, 9, 7};
Circle(7) = {7, 9, 3};
Circle(8) = {3, 9, 8};
Line Loop(11) = {1, 2, 3, 4, -8, -7, -6, -5};
Plane Surface(11) = {11};
